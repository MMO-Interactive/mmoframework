using System;
using System.Collections.Generic;
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
    private const float MaxInputDeltaSeconds = 0.1f;
    private const float MaxMoveMagnitude = 1f;
    private const float MinMoveMagnitude = 0.05f;
    private const float PlayerMoveSpeed = 6f;
    private readonly ZoneDefinition _definition;
    private readonly ZoneDirectory _zoneDirectory;
    private readonly SessionRegistry _sessionRegistry;
    private readonly GhostRegistry _ghostRegistry;
    private readonly ZoneRuntimeSettings _settings;
    private readonly Func<int, CancellationToken, Task> _ensureZoneRunningAsync;
    private readonly ConcurrentDictionary<string, ZoneMob> _mobs;
    private readonly ZoneResourceNode[] _resourceNodes;
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<Guid, ZonePlayer> _players = new();
    private readonly TaskCompletionSource<bool> _tcpReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _udpReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly float _aoiRadiusSquared;
    private readonly float _spatialCellSize;
    private readonly SpatialIndex<ZoneResourceNode> _resourceNodeIndex;
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
        _mobs = CreateDefaultMobs(definition, settings.GetMobCountForZone(definition.ZoneId));
        _resourceNodes = CreateDefaultResourceNodes(definition);
        _aoiRadiusSquared = settings.AoiRadius * settings.AoiRadius;
        _spatialCellSize = MathF.Max(settings.AoiRadius, 1f);
        _resourceNodeIndex = SpatialIndex<ZoneResourceNode>.Build(_resourceNodes, _spatialCellSize, static node => node.Position);
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
                .ToArray(),
            _mobs.Values
                .OrderBy(mob => mob.MobId)
                .Select(mob => new ZoneMobSnapshot(
                    mob.MobId,
                    mob.MobTypeId,
                    mob.Position.X,
                    mob.Position.Y,
                    mob.Position.Z,
                    mob.Velocity.X,
                    mob.Velocity.Y,
                    mob.Velocity.Z,
                    mob.State))
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
                _sessionRegistry.TouchTcp(attach.SessionId);
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
                    if (message is HeartbeatMessage heartbeat)
                    {
                        _sessionRegistry.TouchTcp(attach.SessionId);
                        await player.ControlConnection.SendAsync(new HeartbeatMessage(heartbeat.ServerTicks), cancellationToken).ConfigureAwait(false);
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
                        if (input.Sequence <= player.LastAcceptedInputSequence)
                        {
                            if (input.Sequence % 20 == 0)
                            {
                                Console.WriteLine($"Zone {_definition.ZoneId} rejected out-of-order input seq {input.Sequence} for session {input.SessionId}; last accepted {player.LastAcceptedInputSequence}.");
                            }
                            break;
                        }

                        player.LastAcceptedInputSequence = input.Sequence;
                        player.RemoteEndpoint = result.RemoteEndPoint;
                        var normalizedMove = NormalizeMoveInput(input.Move);
                        player.Velocity = normalizedMove * PlayerMoveSpeed;
                        player.Position += player.Velocity * Math.Clamp(input.DeltaTimeSeconds, 0f, MaxInputDeltaSeconds);
                        player.Position = new NetworkVector3(player.Position.X, 0f, player.Position.Z);
                        player.Position = _definition.Clamp(player.Position);
                        _sessionRegistry.SetCurrentZone(player.SessionId, _definition.ZoneId, player.Position);
                        _sessionRegistry.TouchUdp(player.SessionId);
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
                        _sessionRegistry.TouchUdp(probe.SessionId);
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
                        _sessionRegistry.TouchUdp(probe.SessionId);
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
                UpdateMobs(0.05f);
                var playerList = _players.Values.ToArray();
                foreach (var entry in playerList)
                {
                    PublishGhosts(entry);
                    await MaybeTransferAsync(entry, cancellationToken).ConfigureAwait(false);
                }

                var nowUtc = DateTimeOffset.UtcNow;
                var ghostSnapshots = _ghostRegistry.GetActiveGhosts(_definition.ZoneId, nowUtc);
                var playerIndex = SpatialIndex<ZonePlayer>.Build(playerList, _spatialCellSize, static player => player.Position);
                var ghostIndex = SpatialIndex<GhostRegistry.GhostReplica>.Build(ghostSnapshots, _spatialCellSize, static ghost => ghost.Position);
                var mobList = _mobs.Values.ToArray();
                var mobIndex = SpatialIndex<ZoneMob>.Build(mobList, _spatialCellSize, static mob => mob.Position);
                foreach (var player in playerList)
                {
                    if (player.RemoteEndpoint is null)
                    {
                        continue;
                    }

                    var snapshot = BuildSnapshotForPlayer(player, playerIndex, ghostIndex, mobIndex);
                    var payload = WireProtocol.SerializeUdpMessage(snapshot);
                    await _udpClient.SendAsync(payload, payload.Length, player.RemoteEndpoint).ConfigureAwait(false);
                }

                foreach (var stalePlayer in playerList.Where(player => !_sessionRegistry.TryGet(player.SessionId, out var session) || session!.CurrentZoneId != _definition.ZoneId).ToArray())
                {
                    _players.TryRemove(stalePlayer.SessionId, out _);
                }

                foreach (var timedOutPlayer in playerList.Where(player => _sessionRegistry.IsTimedOut(player.SessionId, _settings.SessionTimeout)).ToArray())
                {
                    if (_sessionRegistry.BeginDisconnectGrace(timedOutPlayer.SessionId, _settings.ReconnectGracePeriod, out var deadlineUtc))
                    {
                        Console.WriteLine($"Zone {_definition.ZoneId} session {timedOutPlayer.SessionId} for player {timedOutPlayer.PlayerId} entered reconnect grace until {deadlineUtc:O}.");
                        if (timedOutPlayer.ControlConnection is not null)
                        {
                            try
                            {
                                await timedOutPlayer.ControlConnection.SendAsync(
                                    new DisconnectNoticeMessage("Connection stalled. Waiting for reconnect grace.", true, (int)_settings.ReconnectGracePeriod.TotalSeconds),
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                            }
                        }
                    }
                }

                foreach (var expiredGracePlayer in playerList.Where(player => _sessionRegistry.IsGraceExpired(player.SessionId, out _)).ToArray())
                {
                    Console.WriteLine($"Zone {_definition.ZoneId} finalizing disconnect for session {expiredGracePlayer.SessionId} after reconnect grace expired.");
                    _players.TryRemove(expiredGracePlayer.SessionId, out _);
                    _sessionRegistry.TryRemove(expiredGracePlayer.SessionId, out _);
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

    private WorldSnapshotMessage BuildSnapshotForPlayer(
        ZonePlayer recipient,
        SpatialIndex<ZonePlayer> playerIndex,
        SpatialIndex<GhostRegistry.GhostReplica> ghostIndex,
        SpatialIndex<ZoneMob> mobIndex)
    {
        var playerSnapshots = new List<PlayerSnapshot>(8);
        foreach (var player in playerIndex.EnumerateNearby(recipient.Position))
        {
            if (player.SessionId != recipient.SessionId && !IsWithinAoi(recipient.Position, player.Position))
            {
                continue;
            }

            playerSnapshots.Add(new PlayerSnapshot(
                player.PlayerId,
                player.Position,
                player.Velocity,
                SnapshotEntityKind.Player,
                _definition.ZoneId));
        }

        foreach (var ghost in ghostIndex.EnumerateNearby(recipient.Position))
        {
            if (ghost.SessionId == recipient.SessionId || _players.ContainsKey(ghost.SessionId) || !IsWithinAoi(recipient.Position, ghost.Position))
            {
                continue;
            }

            playerSnapshots.Add(new PlayerSnapshot(
                ghost.PlayerId,
                ghost.Position,
                ghost.Velocity,
                SnapshotEntityKind.Ghost,
                ghost.SourceZoneId));
        }

        playerSnapshots.Sort(static (left, right) => left.PlayerId.CompareTo(right.PlayerId));

        var visibleNodes = new List<ResourceNodeSnapshot>(4);
        foreach (var node in _resourceNodeIndex.EnumerateNearby(recipient.Position))
        {
            if (!IsWithinAoi(recipient.Position, node.Position))
            {
                continue;
            }

            visibleNodes.Add(new ResourceNodeSnapshot(
                node.NodeId,
                node.ResourceId,
                node.Position,
                node.Remaining,
                node.MaxAmount));
        }

        var visibleMobs = new List<MobSnapshot>(6);
        foreach (var mob in mobIndex.EnumerateNearby(recipient.Position))
        {
            if (!IsWithinAoi(recipient.Position, mob.Position))
            {
                continue;
            }

            visibleMobs.Add(new MobSnapshot(
                mob.MobId,
                mob.MobTypeId,
                mob.Position,
                mob.Velocity,
                mob.State));
        }

        visibleMobs.Sort(static (left, right) => string.CompareOrdinal(left.MobId, right.MobId));

        return new WorldSnapshotMessage(
            _definition.ZoneId,
            _tick,
            playerSnapshots.ToArray(),
            visibleNodes.ToArray(),
            visibleMobs.ToArray());
    }

    private bool IsWithinAoi(NetworkVector3 origin, NetworkVector3 target)
    {
        var dx = origin.X - target.X;
        var dz = origin.Z - target.Z;
        return (dx * dx) + (dz * dz) <= _aoiRadiusSquared;
    }

    private static NetworkVector3 NormalizeMoveInput(NetworkVector3 move)
    {
        var planarMagnitudeSq = (move.X * move.X) + (move.Z * move.Z);
        if (planarMagnitudeSq < MinMoveMagnitude * MinMoveMagnitude)
        {
            return NetworkVector3.Zero;
        }

        if (planarMagnitudeSq <= MaxMoveMagnitude * MaxMoveMagnitude)
        {
            return new NetworkVector3(move.X, 0f, move.Z);
        }

        var planarMagnitude = MathF.Sqrt(planarMagnitudeSq);
        if (planarMagnitude <= 0.0001f)
        {
            return NetworkVector3.Zero;
        }

        var scale = MaxMoveMagnitude / planarMagnitude;
        return new NetworkVector3(move.X * scale, 0f, move.Z * scale);
    }

    private void UpdateMobs(float deltaSeconds)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var mob in _mobs.Values)
        {
            var toTargetX = mob.TargetPosition.X - mob.Position.X;
            var toTargetZ = mob.TargetPosition.Z - mob.Position.Z;
            var distanceSq = (toTargetX * toTargetX) + (toTargetZ * toTargetZ);

            if (distanceSq <= 0.25f)
            {
                mob.Position = new NetworkVector3(mob.TargetPosition.X, 0f, mob.TargetPosition.Z);
                mob.Velocity = NetworkVector3.Zero;

                if (nowUtc >= mob.NextDecisionUtc)
                {
                    mob.TargetPosition = ChooseMobTarget(mob);
                    mob.NextDecisionUtc = nowUtc.AddSeconds(Random.Shared.NextDouble() * 3.0 + 2.0);
                    mob.State = "Wandering";
                }
                else
                {
                    mob.State = "Idle";
                }

                continue;
            }

            var distance = MathF.Sqrt(distanceSq);
            var step = MathF.Min(distance, mob.Speed * deltaSeconds);
            var nx = toTargetX / MathF.Max(distance, 0.0001f);
            var nz = toTargetZ / MathF.Max(distance, 0.0001f);
            mob.Velocity = new NetworkVector3(nx * mob.Speed, 0f, nz * mob.Speed);
            mob.Position = new NetworkVector3(
                mob.Position.X + nx * step,
                0f,
                mob.Position.Z + nz * step);
            mob.State = "Wandering";
        }
    }

    private NetworkVector3 ChooseMobTarget(ZoneMob mob)
    {
        var radius = mob.WanderRadius;
        var x = mob.SpawnPosition.X + (float)((Random.Shared.NextDouble() * 2.0 - 1.0) * radius);
        var z = mob.SpawnPosition.Z + (float)((Random.Shared.NextDouble() * 2.0 - 1.0) * radius);
        return _definition.Clamp(new NetworkVector3(x, 0f, z));
    }

    private static ConcurrentDictionary<string, ZoneMob> CreateDefaultMobs(ZoneDefinition definition, int mobCount)
    {
        var mobs = new ConcurrentDictionary<string, ZoneMob>();
        if (mobCount <= 0)
        {
            return mobs;
        }

        var width = MathF.Max(definition.MaxX - definition.MinX - 12f, 1f);
        var depth = MathF.Max(definition.MaxZ - definition.MinZ - 12f, 1f);
        var columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(mobCount)));
        var rows = Math.Max(1, (int)MathF.Ceiling(mobCount / (float)columns));
        var spacingX = width / columns;
        var spacingZ = depth / rows;

        for (var index = 0; index < mobCount; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var mobTypeId = index % 2 == 0 ? "wolf" : "boar";
            var speed = mobTypeId == "wolf" ? 1.6f : 1.25f;
            var wanderRadius = mobTypeId == "wolf" ? 10f : 8f;
            var spawn = new NetworkVector3(
                definition.MinX + 6f + (column * spacingX) + (spacingX * 0.5f),
                0f,
                definition.MinZ + 6f + (row * spacingZ) + (spacingZ * 0.5f));
            var mobId = mobTypeId + "-" + definition.ZoneId + "-" + (index + 1);
            mobs[mobId] = new ZoneMob(mobId, mobTypeId, definition.Clamp(spawn), speed, wanderRadius);
        }

        return mobs;
    }

    private static ZoneResourceNode[] CreateDefaultResourceNodes(ZoneDefinition definition)
    {
        var midZ = (definition.MinZ + definition.MaxZ) * 0.5f;
        return new[]
        {
            new ZoneResourceNode(
                "tree-1",
                "log",
                new NetworkVector3(definition.MinX + 8f, 0f, midZ - 6f),
                remaining: 6,
                maxAmount: 6),
            new ZoneResourceNode(
                "ore-1",
                "ore",
                new NetworkVector3(definition.MinX + 14f, 0f, midZ + 6f),
                remaining: 5,
                maxAmount: 5)
        };
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
        public uint LastAcceptedInputSequence { get; set; }
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

    private sealed class ZoneMob
    {
        public ZoneMob(string mobId, string mobTypeId, NetworkVector3 spawnPosition, float speed, float wanderRadius)
        {
            MobId = mobId;
            MobTypeId = mobTypeId;
            SpawnPosition = spawnPosition;
            Position = spawnPosition;
            TargetPosition = spawnPosition;
            Speed = speed;
            WanderRadius = wanderRadius;
            State = "Idle";
            NextDecisionUtc = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.NextDouble() * 2.0 + 1.0);
        }

        public string MobId { get; }
        public string MobTypeId { get; }
        public NetworkVector3 SpawnPosition { get; }
        public NetworkVector3 Position { get; set; }
        public NetworkVector3 Velocity { get; set; }
        public NetworkVector3 TargetPosition { get; set; }
        public DateTimeOffset NextDecisionUtc { get; set; }
        public float Speed { get; }
        public float WanderRadius { get; }
        public string State { get; set; }
    }

    private sealed class ZoneResourceNode
    {
        public ZoneResourceNode(string nodeId, string resourceId, NetworkVector3 position, int remaining, int maxAmount)
        {
            NodeId = nodeId;
            ResourceId = resourceId;
            Position = position;
            Remaining = remaining;
            MaxAmount = maxAmount;
        }

        public string NodeId { get; }
        public string ResourceId { get; }
        public NetworkVector3 Position { get; }
        public int Remaining { get; }
        public int MaxAmount { get; }
    }

    private readonly record struct CellKey(int X, int Z);

    private sealed class SpatialIndex<T>
    {
        private readonly Dictionary<CellKey, List<T>> _buckets;
        private readonly float _cellSize;

        private SpatialIndex(Dictionary<CellKey, List<T>> buckets, float cellSize)
        {
            _buckets = buckets;
            _cellSize = cellSize;
        }

        public static SpatialIndex<T> Build(IEnumerable<T> items, float cellSize, Func<T, NetworkVector3> positionSelector)
        {
            var buckets = new Dictionary<CellKey, List<T>>();
            foreach (var item in items)
            {
                var position = positionSelector(item);
                var key = new CellKey(
                    (int)MathF.Floor(position.X / cellSize),
                    (int)MathF.Floor(position.Z / cellSize));

                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<T>();
                    buckets.Add(key, bucket);
                }

                bucket.Add(item);
            }

            return new SpatialIndex<T>(buckets, cellSize);
        }

        public IEnumerable<T> EnumerateNearby(NetworkVector3 origin)
        {
            var cellX = (int)MathF.Floor(origin.X / _cellSize);
            var cellZ = (int)MathF.Floor(origin.Z / _cellSize);

            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (_buckets.TryGetValue(new CellKey(cellX + dx, cellZ + dz), out var bucket))
                    {
                        foreach (var item in bucket)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }
    }
}
