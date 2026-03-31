using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMONetworking.ZoneServer
{
public sealed class UnityZoneServerRuntime : IDisposable
{
    private readonly ZoneDefinition _definition;
    private readonly string _controlHost;
    private readonly int _controlPort;
    private readonly float _prewarmMargin;
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<Guid, ZonePlayer> _players = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ResourceNode> _resourceNodes;
    private readonly object _resourceSync = new();
    private uint _tick;

    public UnityZoneServerRuntime(ZoneDefinition definition, string controlHost, int controlPort, float prewarmMargin)
    {
        _definition = definition;
        _controlHost = controlHost;
        _controlPort = controlPort;
        _prewarmMargin = prewarmMargin;
        _tcpListener = new TcpListener(IPAddress.Any, definition.TcpPort);
        _udpClient = new UdpClient(definition.UdpPort);
        _resourceNodes = CreateDefaultResourceNodes(definition);
    }

    public int ActivePlayers => _players.Count;
    public uint Tick => _tick;
    public string Summary => $"Zone {_definition.ZoneId} {_definition.Name} | players {_players.Count} | tick {_tick}";

    public Task RunAsync()
        => Task.WhenAll(RunTcpAsync(_shutdown.Token), RunUdpAsync(_shutdown.Token), RunTickAsync(_shutdown.Token));

    public void Dispose()
    {
        _shutdown.Cancel();
        _udpClient.Dispose();
        _tcpListener.Stop();
        _shutdown.Dispose();
    }

    private async Task RunTcpAsync(CancellationToken cancellationToken)
    {
        _tcpListener.Start();
        Debug.Log($"Unity zone server TCP listening on {_definition.TcpPort}.");
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _tcpListener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                throw;
            }

            _ = Task.Run(() => HandleTcpClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var firstMessage = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!(firstMessage is AttachToZoneMessage attach))
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Expected AttachToZone."), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var authorized = await AuthorizeAttachAsync(attach, cancellationToken).ConfigureAwait(false);
                if (!authorized.Success)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage(authorized.ErrorText), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var player = _players.GetOrAdd(
                    attach.SessionId,
                    _ => new ZonePlayer(authorized.PlayerId, attach.SessionId, authorized.SpawnPosition));
                player.Position = authorized.SpawnPosition;
                player.PendingDestinationZoneId = null;
                player.LastIssuedTransferId = null;
                player.GatheringSkill = Math.Max(player.GatheringSkill, 1);

                player.ControlStream = stream;
                await WireProtocol.WriteTcpMessageAsync(
                    stream,
                    new AttachAcceptedMessage(attach.SessionId, _definition.ZoneId, player.Position),
                    cancellationToken).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (message is HeartbeatMessage)
                    {
                        continue;
                    }

                    var gameplayCommand = message as GameplayCommandMessage;
                    if (gameplayCommand != null)
                    {
                        await HandleGameplayCommandAsync(player, gameplayCommand, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task RunUdpAsync(CancellationToken cancellationToken)
    {
        Debug.Log($"Unity zone server UDP listening on {_definition.UdpPort}.");
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udpClient.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                throw;
            }

            UdpMessage message;
            try
            {
                message = WireProtocol.DeserializeUdpMessage(result.Buffer);
            }
            catch
            {
                continue;
            }

            if (message is ClientInputMessage input)
            {
                ZonePlayer player;
                if (_players.TryGetValue(input.SessionId, out player))
                {
                    player.RemoteEndpoint = result.RemoteEndPoint;
                    player.Velocity = input.Move * 6f;
                    player.Position += player.Velocity * Mathf.Clamp(input.DeltaTimeSeconds, 0f, 0.1f);
                    player.Position = new NetworkVector3(player.Position.X, 0f, player.Position.Z);
                    _ = ReportStateAsync(player.SessionId, player.Position, cancellationToken);
                }
            }
            else if (message is TransferProbeMessage probe)
            {
                ZonePlayer player;
                if (_players.TryGetValue(probe.SessionId, out player))
                {
                    player.RemoteEndpoint = result.RemoteEndPoint;
                    var ack = WireProtocol.SerializeUdpMessage(new TransferReadyMessage(probe.SessionId, _definition.ZoneId));
                    await _udpClient.SendAsync(ack, ack.Length, result.RemoteEndPoint).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            _tick++;
            RegenerateResourceNodes();

            foreach (var player in _players.Values.ToArray())
            {
                await MaybePrewarmAsync(player, cancellationToken).ConfigureAwait(false);
                await MaybeTransferAsync(player, cancellationToken).ConfigureAwait(false);
            }

            var snapshot = new WorldSnapshotMessage(
                _definition.ZoneId,
                _tick,
                _players.Values.Select(player => new PlayerSnapshot(
                    player.PlayerId,
                    player.Position,
                    player.Velocity,
                    SnapshotEntityKind.Player,
                    _definition.ZoneId)).ToArray());

            var payload = WireProtocol.SerializeUdpMessage(snapshot);
            foreach (var player in _players.Values)
            {
                if (player.RemoteEndpoint == null)
                {
                    continue;
                }

                await _udpClient.SendAsync(payload, payload.Length, player.RemoteEndpoint).ConfigureAwait(false);
            }

            foreach (var pair in _players.ToArray())
            {
                if (pair.Value.PendingDestinationZoneId.HasValue && pair.Value.PendingDestinationZoneId.Value != _definition.ZoneId)
                {
                    _players.TryRemove(pair.Key, out _);
                }
            }
        }
    }

    private async Task HandleGameplayCommandAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        if (command.SessionId != player.SessionId)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Session mismatch.", string.Empty, 0, player.GatheringSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (command.CommandKind)
        {
            case GameplayCommandKind.Gather:
                await HandleGatherAsync(player, command, cancellationToken).ConfigureAwait(false);
                return;
            case GameplayCommandKind.InspectInventory:
                await SendToPlayerAsync(
                    player,
                    new GameplayResultMessage(
                        command.SessionId,
                        command.CommandId,
                        true,
                        BuildInventorySummary(player),
                        string.Empty,
                        0,
                        player.GatheringSkill),
                    cancellationToken).ConfigureAwait(false);
                return;
            default:
                await SendToPlayerAsync(
                    player,
                    new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unsupported gameplay command.", string.Empty, 0, player.GatheringSkill),
                    cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandleGatherAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < player.NextGatherAllowedUtc)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Gather on cooldown.", string.Empty, 0, player.GatheringSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        ResourceNode node;
        lock (_resourceSync)
        {
            _resourceNodes.TryGetValue(command.TargetId, out node);
        }

        if (node == null)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unknown resource node.", string.Empty, 0, player.GatheringSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var distanceSq = DistanceSquared(player.Position, node.Position);
        if (distanceSq > 25f)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Too far away to gather.", string.Empty, 0, player.GatheringSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var gathered = false;
        lock (_resourceSync)
        {
            if (node.Remaining > 0)
            {
                node.Remaining--;
                node.LastHarvestedUtc = DateTimeOffset.UtcNow;
                gathered = true;
            }
        }

        if (!gathered)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Node is depleted.", node.ItemId, player.GetItemCount(node.ItemId), player.GatheringSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var amount = Math.Max(1, player.GatheringSkill / 10);
        player.AddItem(node.ItemId, amount);
        player.GatherAttempts++;
        if (player.GatherAttempts % 3 == 0)
        {
            player.GatheringSkill++;
        }

        player.NextGatherAllowedUtc = DateTimeOffset.UtcNow.AddMilliseconds(750);
        await SendToPlayerAsync(
            player,
            new GameplayResultMessage(
                command.SessionId,
                command.CommandId,
                true,
                "Gathered " + amount + " " + node.ItemId + ".",
                node.ItemId,
                player.GetItemCount(node.ItemId),
                player.GatheringSkill),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendToPlayerAsync(ZonePlayer player, TcpMessage message, CancellationToken cancellationToken)
    {
        var stream = player.ControlStream;
        if (stream == null)
        {
            return;
        }

        await player.ControlWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WireProtocol.WriteTcpMessageAsync(stream, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            player.ControlWriteLock.Release();
        }
    }

    private void RegenerateResourceNodes()
    {
        lock (_resourceSync)
        {
            foreach (var node in _resourceNodes.Values)
            {
                if (node.Remaining >= node.MaxAmount)
                {
                    continue;
                }

                if (DateTimeOffset.UtcNow - node.LastHarvestedUtc >= TimeSpan.FromSeconds(8))
                {
                    node.Remaining++;
                }
            }
        }
    }

    private static Dictionary<string, ResourceNode> CreateDefaultResourceNodes(ZoneDefinition definition)
    {
        var midZ = (definition.MinZ + definition.MaxZ) * 0.5f;
        return new Dictionary<string, ResourceNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["tree-1"] = new ResourceNode("tree-1", "log", new NetworkVector3(definition.MinX + 8f, 0f, midZ - 6f), 6),
            ["ore-1"] = new ResourceNode("ore-1", "ore", new NetworkVector3(definition.MinX + 14f, 0f, midZ + 6f), 5)
        };
    }

    private static float DistanceSquared(NetworkVector3 a, NetworkVector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    private static string BuildInventorySummary(ZonePlayer player)
    {
        var logCount = player.GetItemCount("log");
        var oreCount = player.GetItemCount("ore");
        return "Inventory -> log: " + logCount + ", ore: " + oreCount + " | Gathering " + player.GatheringSkill;
    }

    private async Task MaybeTransferAsync(ZonePlayer player, CancellationToken cancellationToken)
    {
        if (_definition.Contains(player.Position) || player.PendingDestinationZoneId.HasValue || player.ControlStream == null)
        {
            return;
        }

        Debug.Log("Unity zone " + _definition.ZoneId + " requesting transfer for session " + player.SessionId + " at " + player.Position.X.ToString("F2") + "," + player.Position.Z.ToString("F2") + ".");
        var response = await RequestTransferAsync(player.SessionId, player.Position, cancellationToken).ConfigureAwait(false);
        if (!response.ShouldTransfer)
        {
            player.Position = _definition.Clamp(player.Position);
            Debug.Log("Unity zone " + _definition.ZoneId + " kept player " + player.PlayerId + " in-zone after transfer check.");
            return;
        }

        player.PendingDestinationZoneId = response.ToZoneId;
        player.LastIssuedTransferId = Guid.NewGuid();
        Debug.Log("Unity zone " + _definition.ZoneId + " transferring player " + player.PlayerId + " to zone " + response.ToZoneId + ".");
        try
        {
            await SendToPlayerAsync(
                player,
                new ZoneTransferPrepareMessage(
                    player.SessionId,
                    player.LastIssuedTransferId.Value,
                    response.FromZoneId,
                    response.ToZoneId,
                    response.ZoneHost,
                    response.ZoneTcpPort,
                    response.ZoneUdpPort,
                    response.TransferToken,
                    response.SpawnPosition),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.LogError("Unity zone " + _definition.ZoneId + " failed to send transfer prepare: " + ex);
            player.PendingDestinationZoneId = null;
        }
    }

    private async Task MaybePrewarmAsync(ZonePlayer player, CancellationToken cancellationToken)
    {
        if (player.PendingDestinationZoneId.HasValue)
        {
            return;
        }

        var destinationZoneId = GetAdjacentZoneIdWithinMargin(player.Position, _prewarmMargin);
        if (!destinationZoneId.HasValue)
        {
            player.LastPrewarmedZoneId = null;
            return;
        }

        if (player.LastPrewarmedZoneId == destinationZoneId.Value)
        {
            return;
        }

        player.LastPrewarmedZoneId = destinationZoneId.Value;
        try
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(_controlHost, _controlPort).ConfigureAwait(false);
                using (var stream = client.GetStream())
                {
                    await WireProtocol.WriteTcpMessageAsync(
                        stream,
                        new ZonePrewarmRequestMessage(player.SessionId, _definition.ZoneId, player.Position),
                        cancellationToken).ConfigureAwait(false);

                    var response = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (response is ZonePrewarmResponseMessage prewarm && prewarm.Started)
                    {
                        Debug.Log("Unity zone " + _definition.ZoneId + " prewarmed zone " + prewarm.DestinationZoneId + " for player " + player.PlayerId + ".");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Zone prewarm failed: " + ex.Message);
            player.LastPrewarmedZoneId = null;
        }
    }

    private async Task<ZoneAttachAuthorizedMessage> AuthorizeAttachAsync(AttachToZoneMessage attach, CancellationToken cancellationToken)
    {
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(_controlHost, _controlPort).ConfigureAwait(false);
            using (var stream = client.GetStream())
            {
                await WireProtocol.WriteTcpMessageAsync(
                    stream,
                    new ZoneAttachAuthorizeMessage(attach.SessionId, attach.PlayerId, _definition.ZoneId, attach.TransferToken),
                    cancellationToken).ConfigureAwait(false);

                var response = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (response is ZoneAttachAuthorizedMessage authorized)
                {
                    Debug.Log("Unity zone " + _definition.ZoneId + " attach authorization " + (authorized.Success ? "accepted" : "rejected") + " for session " + attach.SessionId + ".");
                    return authorized;
                }

                if (response is ErrorMessage error)
                {
                    return new ZoneAttachAuthorizedMessage(false, attach.SessionId, attach.PlayerId, _definition.ZoneId, NetworkVector3.Zero, error.Text);
                }

                return new ZoneAttachAuthorizedMessage(false, attach.SessionId, attach.PlayerId, _definition.ZoneId, NetworkVector3.Zero, "Unexpected control response.");
            }
        }
    }

    private async Task ReportStateAsync(Guid sessionId, NetworkVector3 position, CancellationToken cancellationToken)
    {
        try
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(_controlHost, _controlPort).ConfigureAwait(false);
                using (var stream = client.GetStream())
                {
                    await WireProtocol.WriteTcpMessageAsync(
                        stream,
                        new ZoneStateUpdateMessage(sessionId, _definition.ZoneId, position),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Zone state update failed: " + ex.Message);
        }
    }

    private async Task<ZoneTransferResponseMessage> RequestTransferAsync(Guid sessionId, NetworkVector3 position, CancellationToken cancellationToken)
    {
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(_controlHost, _controlPort).ConfigureAwait(false);
            using (var stream = client.GetStream())
            {
                await WireProtocol.WriteTcpMessageAsync(
                    stream,
                    new ZoneTransferRequestMessage(sessionId, _definition.ZoneId, position),
                    cancellationToken).ConfigureAwait(false);

                var response = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (response is ZoneTransferResponseMessage transferResponse)
                {
                    Debug.Log("Unity zone " + _definition.ZoneId + " transfer response: shouldTransfer=" + transferResponse.ShouldTransfer + " to zone " + transferResponse.ToZoneId + ".");
                    return transferResponse;
                }

                if (response is ErrorMessage error)
                {
                    return new ZoneTransferResponseMessage(false, sessionId, _definition.ZoneId, _definition.ZoneId, string.Empty, 0, 0, string.Empty, position, error.Text);
                }

                return new ZoneTransferResponseMessage(false, sessionId, _definition.ZoneId, _definition.ZoneId, string.Empty, 0, 0, string.Empty, position, "Unexpected control response.");
            }
        }
    }

    private int? GetAdjacentZoneIdWithinMargin(NetworkVector3 position, float margin)
    {
        if (_definition.MaxX - position.X <= margin)
        {
            return _definition.ZoneId + 1;
        }

        if (position.X - _definition.MinX <= margin)
        {
            return _definition.ZoneId - 1;
        }

        return null;
    }

    private sealed class ZonePlayer
    {
        public ZonePlayer(ulong playerId, Guid sessionId, NetworkVector3 position)
        {
            PlayerId = playerId;
            SessionId = sessionId;
            Position = position;
            Velocity = NetworkVector3.Zero;
        }

        public ulong PlayerId { get; }
        public Guid SessionId { get; }
        public NetworkVector3 Position { get; set; }
        public NetworkVector3 Velocity { get; set; }
        public IPEndPoint RemoteEndpoint { get; set; }
        public Stream ControlStream { get; set; }
        public int? PendingDestinationZoneId { get; set; }
        public Guid? LastIssuedTransferId { get; set; }
        public int? LastPrewarmedZoneId { get; set; }
        public int GatheringSkill { get; set; }
        public int GatherAttempts { get; set; }
        public DateTimeOffset NextGatherAllowedUtc { get; set; }
        public SemaphoreSlim ControlWriteLock { get; } = new SemaphoreSlim(1, 1);

        private readonly Dictionary<string, int> _inventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public void AddItem(string itemId, int amount)
        {
            int existing;
            _inventory.TryGetValue(itemId, out existing);
            _inventory[itemId] = existing + amount;
        }

        public int GetItemCount(string itemId)
        {
            int count;
            return _inventory.TryGetValue(itemId, out count) ? count : 0;
        }
    }

    private sealed class ResourceNode
    {
        public ResourceNode(string nodeId, string itemId, NetworkVector3 position, int maxAmount)
        {
            NodeId = nodeId;
            ItemId = itemId;
            Position = position;
            MaxAmount = maxAmount;
            Remaining = maxAmount;
            LastHarvestedUtc = DateTimeOffset.UtcNow;
        }

        public string NodeId { get; }
        public string ItemId { get; }
        public NetworkVector3 Position { get; }
        public int MaxAmount { get; }
        public int Remaining { get; set; }
        public DateTimeOffset LastHarvestedUtc { get; set; }
    }
}
}
