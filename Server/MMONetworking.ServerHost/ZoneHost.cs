using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class ZoneHost : IDisposable
{
    private readonly ZoneDefinition _definition;
    private readonly ZoneDirectory _zoneDirectory;
    private readonly SessionRegistry _sessionRegistry;
    private readonly GhostRegistry _ghostRegistry;
    private readonly ZoneRuntimeSettings _settings;
    private readonly Func<int, CancellationToken, Task> _ensureZoneRunningAsync;
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<Guid, ZonePlayer> _players = new();
    private readonly TaskCompletionSource<bool> _tcpReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _udpReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _tick;
    private long _totalTransfersInitiated;

    public ZoneHost(
        ZoneDefinition definition,
        ZoneDirectory zoneDirectory,
        SessionRegistry sessionRegistry,
        GhostRegistry ghostRegistry,
        ZoneRuntimeSettings settings,
        Func<int, CancellationToken, Task> ensureZoneRunningAsync)
    {
        _definition = definition;
        _zoneDirectory = zoneDirectory;
        _sessionRegistry = sessionRegistry;
        _ghostRegistry = ghostRegistry;
        _settings = settings;
        _ensureZoneRunningAsync = ensureZoneRunningAsync;
        _tcpListener = new TcpListener(IPAddress.Any, definition.TcpPort);
        _udpClient = new UdpClient(definition.UdpPort);
    }

    public int ActivePlayerCount => _players.Count;
    public Task Ready => Task.WhenAll(_tcpReady.Task, _udpReady.Task);

    public Task RunAsync(CancellationToken cancellationToken)
        => Task.WhenAll(
            RunTcpAsync(cancellationToken),
            RunUdpAsync(cancellationToken),
            RunTickAsync(cancellationToken));

    public ZoneRuntimeSnapshot CreateDashboardSnapshot(string lifecycleState, DateTimeOffset lastStateChangeUtc, DateTimeOffset? idleSinceUtc)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        return new ZoneRuntimeSnapshot(
            _definition.ZoneId,
            _definition.Name,
            _definition.TcpPort,
            _definition.UdpPort,
            _definition.MinX,
            _definition.MaxX,
            _definition.MinZ,
            _definition.MaxZ,
            "InProcess",
            lifecycleState,
            lastStateChangeUtc,
            idleSinceUtc,
            _tick,
            _players.Count,
            _ghostRegistry.GetActiveGhostCount(_definition.ZoneId, nowUtc),
            Interlocked.Read(ref _totalTransfersInitiated),
            _settings.AoiRadius,
            _settings.GhostMargin,
            _settings.PrewarmMargin,
            _settings.TransferInset,
            _players.Values
                .OrderBy(player => player.PlayerId)
                .Select(player => new ZonePlayerSnapshot(
                    player.SessionId,
                    player.PlayerId,
                    player.Position.X,
                    player.Position.Y,
                    player.Position.Z,
                    player.Velocity.X,
                    player.Velocity.Y,
                    player.Velocity.Z,
                    player.PendingDestinationZoneId))
                .ToArray());
    }

    public void Dispose()
    {
        _tcpListener.Stop();
        _udpClient.Dispose();
    }

    private async Task RunTcpAsync(CancellationToken cancellationToken)
    {
        _tcpListener.Start();
        _tcpReady.TrySetResult(true);
        Console.WriteLine($"Zone {_definition.ZoneId} TCP listening on {_definition.TcpPort}.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleTcpClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var firstMessage = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (firstMessage is not AttachToZoneMessage attach)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Expected AttachToZone."), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!_sessionRegistry.TryConsumeAttachment(attach.SessionId, _definition.ZoneId, attach.TransferToken, out var session, out var pending))
                {
                    Console.WriteLine($"Zone {_definition.ZoneId} rejected TCP attach for session {attach.SessionId}.");
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Invalid transfer token."), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var player = _players.AddOrUpdate(
                    attach.SessionId,
                    _ => new ZonePlayer(session!.PlayerId, attach.SessionId, pending.SpawnPosition),
                    (_, existing) =>
                    {
                        existing.Position = pending.SpawnPosition;
                        existing.PendingTransferId = null;
                        existing.PendingDestinationZoneId = null;
                        return existing;
                    });

                player.ControlConnection = new ZoneControlConnection(stream);
                _sessionRegistry.SetCurrentZone(attach.SessionId, _definition.ZoneId, player.Position);
                Console.WriteLine($"Zone {_definition.ZoneId} TCP attach accepted for session {attach.SessionId}, player {player.PlayerId}, spawn {player.Position.X:F2},{player.Position.Z:F2}.");

                await player.ControlConnection.SendAsync(
                    new AttachAcceptedMessage(attach.SessionId, _definition.ZoneId, player.Position),
                    cancellationToken).ConfigureAwait(false);

                if (player.LastIssuedTransferId is Guid transferId)
                {
                    await player.ControlConnection.SendAsync(
                        new ZoneTransferCommittedMessage(attach.SessionId, transferId, _definition.ZoneId),
                        cancellationToken).ConfigureAwait(false);
                }

                Console.WriteLine($"Zone {_definition.ZoneId} attached player {player.PlayerId}.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (message is HeartbeatMessage)
                    {
                        continue;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Zone {_definition.ZoneId} TCP error: {ex}");
        }
    }

    private async Task RunUdpAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Zone {_definition.ZoneId} UDP listening on {_definition.UdpPort}.");
        _udpReady.TrySetResult(true);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                UdpMessage message;
                try
                {
                    message = WireProtocol.DeserializeUdpMessage(result.Buffer);
                }
                catch
                {
                    continue;
                }

                switch (message)
                {
                    case ClientInputMessage input:
                    if (_players.TryGetValue(input.SessionId, out var player))
                    {
                        player.RemoteEndpoint = result.RemoteEndPoint;
                        player.Velocity = input.Move * 6f;
                        player.Position += player.Velocity * Math.Clamp(input.DeltaTimeSeconds, 0f, 0.1f);
                        player.Position = new NetworkVector3(player.Position.X, 0f, player.Position.Z);
                        _sessionRegistry.SetCurrentZone(player.SessionId, _definition.ZoneId, player.Position);
                        if (input.Sequence % 20 == 0)
                        {
                            Console.WriteLine($"Zone {_definition.ZoneId} UDP input seq {input.Sequence} for session {input.SessionId} from {result.RemoteEndPoint}, pos {player.Position.X:F2},{player.Position.Z:F2}.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Zone {_definition.ZoneId} UDP input dropped for unknown session {input.SessionId} from {result.RemoteEndPoint}.");
                    }

                    break;
                case TransferProbeMessage probe:
                    if (_players.TryGetValue(probe.SessionId, out var existingPlayer))
                    {
                        existingPlayer.RemoteEndpoint = result.RemoteEndPoint;
                        _sessionRegistry.SetCurrentZone(probe.SessionId, _definition.ZoneId, existingPlayer.Position);
                        Console.WriteLine($"Zone {_definition.ZoneId} UDP probe rebound existing session {probe.SessionId} to {result.RemoteEndPoint}.");
                        var existingAck = WireProtocol.SerializeUdpMessage(new TransferReadyMessage(probe.SessionId, _definition.ZoneId));
                        await _udpClient.SendAsync(existingAck, existingAck.Length, result.RemoteEndPoint).ConfigureAwait(false);
                        break;
                    }

                if (_sessionRegistry.TryConsumeAttachment(probe.SessionId, _definition.ZoneId, probe.TransferToken, out var session, out var pending))
                    {
                        var transferPlayer = _players.AddOrUpdate(
                            probe.SessionId,
                            _ => new ZonePlayer(session!.PlayerId, probe.SessionId, pending.SpawnPosition),
                            (_, existing) =>
                            {
                                existing.Position = pending.SpawnPosition;
                                return existing;
                            });

                        transferPlayer.RemoteEndpoint = result.RemoteEndPoint;
                        _sessionRegistry.SetCurrentZone(probe.SessionId, _definition.ZoneId, transferPlayer.Position);
                        Console.WriteLine($"Zone {_definition.ZoneId} UDP probe created session {probe.SessionId} at endpoint {result.RemoteEndPoint}.");
                        var ack = WireProtocol.SerializeUdpMessage(new TransferReadyMessage(probe.SessionId, _definition.ZoneId));
                        await _udpClient.SendAsync(ack, ack.Length, result.RemoteEndPoint).ConfigureAwait(false);
                    }
                    else
                    {
                        Console.WriteLine($"Zone {_definition.ZoneId} UDP probe rejected for session {probe.SessionId} from {result.RemoteEndPoint}.");
                    }

                    break;
            }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                _tick++;
                var playerList = _players.Values.ToArray();
                foreach (var entry in playerList)
                {
                    PublishGhosts(entry);
                    await MaybeTransferAsync(entry, cancellationToken).ConfigureAwait(false);
                }

                var nowUtc = DateTimeOffset.UtcNow;
                var ghostSnapshots = _ghostRegistry.GetActiveGhosts(_definition.ZoneId, nowUtc);
                foreach (var player in playerList)
                {
                    if (player.RemoteEndpoint is null)
                    {
                        continue;
                    }

                    var snapshot = BuildSnapshotForPlayer(player, playerList, ghostSnapshots);
                    var payload = WireProtocol.SerializeUdpMessage(snapshot);
                    await _udpClient.SendAsync(payload, payload.Length, player.RemoteEndpoint).ConfigureAwait(false);
                }

                foreach (var stalePlayer in playerList.Where(player => _sessionRegistry.TryGet(player.SessionId, out var session) && session!.CurrentZoneId != _definition.ZoneId).ToArray())
                {
                    _players.TryRemove(stalePlayer.SessionId, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task MaybeTransferAsync(ZonePlayer player, CancellationToken cancellationToken)
    {
        if (_definition.Contains(player.Position) || player.PendingDestinationZoneId.HasValue)
        {
            return;
        }

        var destination = _zoneDirectory.ResolveDestination(player.Position, _definition.ZoneId);
        if (destination.ZoneId == _definition.ZoneId)
        {
            player.Position = _definition.Clamp(player.Position);
            return;
        }

        if (player.ControlConnection is null)
        {
            return;
        }

        await _ensureZoneRunningAsync(destination.ZoneId, cancellationToken).ConfigureAwait(false);

        var spawn = CalculateTransferSpawn(destination, player.Position);
        var token = _sessionRegistry.IssueTransferToken(player.SessionId, destination.ZoneId, spawn);
        var transferId = Guid.NewGuid();
        player.PendingDestinationZoneId = destination.ZoneId;
        player.PendingTransferId = transferId;
        player.LastIssuedTransferId = transferId;
        Interlocked.Increment(ref _totalTransfersInitiated);

        var message = new ZoneTransferPrepareMessage(
            player.SessionId,
            transferId,
            _definition.ZoneId,
            destination.ZoneId,
            destination.Host,
            destination.TcpPort,
            destination.UdpPort,
            token,
            spawn);

        await player.ControlConnection.SendAsync(message, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Zone {_definition.ZoneId} transferring player {player.PlayerId} to zone {destination.ZoneId}.");
    }

    private void PublishGhosts(ZonePlayer player)
    {
        var destinations = _zoneDirectory.GetGhostDestinations(_definition.ZoneId, player.Position, _settings.GhostMargin);
        foreach (var destination in destinations)
        {
            _ghostRegistry.Publish(
                destination.ZoneId,
                player.SessionId,
                player.PlayerId,
                _definition.ZoneId,
                player.Position,
                player.Velocity,
                ttl: _settings.GhostTtl);
        }
    }

    private NetworkVector3 CalculateTransferSpawn(ZoneDefinition destination, NetworkVector3 currentPosition)
    {
        var spawn = destination.Clamp(currentPosition);

        if (destination.MinX >= _definition.MaxX)
        {
            return new NetworkVector3(destination.MinX + _settings.TransferInset, spawn.Y, spawn.Z);
        }

        if (destination.MaxX <= _definition.MinX)
        {
            return new NetworkVector3(destination.MaxX - _settings.TransferInset, spawn.Y, spawn.Z);
        }

        if (destination.MinZ >= _definition.MaxZ)
        {
            return new NetworkVector3(spawn.X, spawn.Y, destination.MinZ + _settings.TransferInset);
        }

        if (destination.MaxZ <= _definition.MinZ)
        {
            return new NetworkVector3(spawn.X, spawn.Y, destination.MaxZ - _settings.TransferInset);
        }

        return spawn;
    }

    private WorldSnapshotMessage BuildSnapshotForPlayer(ZonePlayer recipient, ZonePlayer[] playerList, GhostRegistry.GhostReplica[] ghostReplicas)
    {
        var visiblePlayers = playerList
            .Where(player => player.SessionId == recipient.SessionId || IsWithinAoi(recipient.Position, player.Position))
            .Select(player => new PlayerSnapshot(
                player.PlayerId,
                player.Position,
                player.Velocity,
                SnapshotEntityKind.Player,
                _definition.ZoneId));

        var visibleGhosts = ghostReplicas
            .Where(ghost => ghost.SessionId != recipient.SessionId)
            .Where(ghost => !_players.ContainsKey(ghost.SessionId))
            .Where(ghost => IsWithinAoi(recipient.Position, ghost.Position))
            .Select(ghost => new PlayerSnapshot(
                ghost.PlayerId,
                ghost.Position,
                ghost.Velocity,
                SnapshotEntityKind.Ghost,
                ghost.SourceZoneId));

        return new WorldSnapshotMessage(
            _definition.ZoneId,
            _tick,
            visiblePlayers
                .Concat(visibleGhosts)
                .OrderBy(snapshot => snapshot.PlayerId)
                .ToArray());
    }

    private bool IsWithinAoi(NetworkVector3 origin, NetworkVector3 target)
    {
        var dx = origin.X - target.X;
        var dz = origin.Z - target.Z;
        return (dx * dx) + (dz * dz) <= _settings.AoiRadius * _settings.AoiRadius;
    }

    private sealed class ZonePlayer
    {
        public ZonePlayer(ulong playerId, Guid sessionId, NetworkVector3 position)
        {
            PlayerId = playerId;
            SessionId = sessionId;
            Position = position;
        }

        public ulong PlayerId { get; }
        public Guid SessionId { get; }
        public NetworkVector3 Position { get; set; }
        public NetworkVector3 Velocity { get; set; }
        public IPEndPoint? RemoteEndpoint { get; set; }
        public Guid? PendingTransferId { get; set; }
        public Guid? LastIssuedTransferId { get; set; }
        public int? PendingDestinationZoneId { get; set; }
        public ZoneControlConnection? ControlConnection { get; set; }
    }

    private sealed class ZoneControlConnection
    {
        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public ZoneControlConnection(Stream stream)
        {
            _stream = stream;
        }

        public async Task SendAsync(TcpMessage message, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WireProtocol.WriteTcpMessageAsync(_stream, message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
