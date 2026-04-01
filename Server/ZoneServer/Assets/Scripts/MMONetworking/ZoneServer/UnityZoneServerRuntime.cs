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
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(12);
    private const float AoiRadius = 36f;
    private const float AoiRadiusSquared = AoiRadius * AoiRadius;
    private const float MaxInputDeltaSeconds = 0.1f;
    private const float MaxMoveMagnitude = 1f;
    private const float MinMoveMagnitude = 0.05f;
    private const float PlayerMoveSpeed = 6f;
    private const int MaxMana = 100;
    private readonly ZoneDefinition _definition;
    private readonly string _controlHost;
    private readonly int _controlPort;
    private readonly float _prewarmMargin;
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<Guid, ZonePlayer> _players = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ResourceNode> _resourceNodes;
    private readonly Dictionary<string, ZoneMob> _mobs;
    private readonly Dictionary<string, FarmPlot> _farmPlots = new Dictionary<string, FarmPlot>(StringComparer.OrdinalIgnoreCase);
    private readonly object _resourceSync = new();
    private readonly object _mobSync = new();
    private readonly object _farmSync = new();
    private readonly object _mobSync = new();
    private readonly System.Random _random = new System.Random();
    private int _mobReportInFlight;
    private int _stateReportInFlight;
    private uint _tick;

    public UnityZoneServerRuntime(ZoneDefinition definition, string controlHost, int controlPort, float prewarmMargin, int mobCount)
    {
        _definition = definition;
        _controlHost = controlHost;
        _controlPort = controlPort;
        _prewarmMargin = prewarmMargin;
        _tcpListener = new TcpListener(IPAddress.Any, definition.TcpPort);
        _udpClient = new UdpClient(definition.UdpPort);
        _resourceNodes = CreateDefaultResourceNodes(definition);
        _mobs = CreateDefaultMobs(definition, mobCount);
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
                player.WoodcuttingSkill = Math.Max(player.WoodcuttingSkill, 1);
                player.MiningSkill = Math.Max(player.MiningSkill, 1);
                player.CraftingSkill = Math.Max(player.CraftingSkill, 1);
                player.FarmingSkill = Math.Max(player.FarmingSkill, 1);
                player.AnimalTamingSkill = Math.Max(player.AnimalTamingSkill, 1);

                player.ControlStream = stream;
                player.LastTcpSeenUtc = DateTimeOffset.UtcNow;
                await WireProtocol.WriteTcpMessageAsync(
                    stream,
                    new AttachAcceptedMessage(attach.SessionId, _definition.ZoneId, player.Position),
                    cancellationToken).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (message is HeartbeatMessage heartbeat)
                    {
                        player.LastTcpSeenUtc = DateTimeOffset.UtcNow;
                        await SendToPlayerAsync(player, new HeartbeatMessage(heartbeat.ServerTicks), cancellationToken).ConfigureAwait(false);
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
                    if (input.Sequence <= player.LastAcceptedInputSequence)
                    {
                        continue;
                    }

                    player.LastAcceptedInputSequence = input.Sequence;
                    player.RemoteEndpoint = result.RemoteEndPoint;
                    player.LastUdpSeenUtc = DateTimeOffset.UtcNow;
                    var normalizedMove = NormalizeMoveInput(input.Move);
                    var speedMultiplier = player.GetMovementSpeedMultiplier(DateTimeOffset.UtcNow);
                    player.Velocity = normalizedMove * (PlayerMoveSpeed * speedMultiplier);
                    player.Position += player.Velocity * Mathf.Clamp(input.DeltaTimeSeconds, 0f, MaxInputDeltaSeconds);
                    player.Position = new NetworkVector3(player.Position.X, 0f, player.Position.Z);
                    player.Position = _definition.Clamp(player.Position);
                }
            }
            else if (message is TransferProbeMessage probe)
            {
                ZonePlayer player;
                if (_players.TryGetValue(probe.SessionId, out player))
                {
                    player.RemoteEndpoint = result.RemoteEndPoint;
                    player.LastUdpSeenUtc = DateTimeOffset.UtcNow;
                    var ack = WireProtocol.SerializeUdpMessage(new TransferReadyMessage(probe.SessionId, _definition.ZoneId));
                    await _udpClient.SendAsync(ack, ack.Length, result.RemoteEndPoint).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                _tick++;
                RegenerateResourceNodes();
                UpdateMobs(0.05f);
                UpdateMagicEffects(0.05f);
                UpdateFarmPlots();

                foreach (var player in _players.Values.ToArray())
                {
                    try
                    {
                        await MaybePrewarmAsync(player, cancellationToken).ConfigureAwait(false);
                        await MaybeTransferAsync(player, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Unity zone " + _definition.ZoneId + " player tick step failed for " + player.PlayerId + ": " + ex.Message);
                    }
                }

                if (_tick % 5 == 0 && Interlocked.CompareExchange(ref _mobReportInFlight, 1, 0) == 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ReportMobsAsync(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _mobReportInFlight, 0);
                        }
                    }, CancellationToken.None);
                }

                if (_tick % 4 == 0 && Interlocked.CompareExchange(ref _stateReportInFlight, 1, 0) == 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ReportStatesAsync(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _stateReportInFlight, 0);
                        }
                    }, CancellationToken.None);
                }

                var playerList = _players.Values.ToArray();
                var playerIndex = SpatialIndex<ZonePlayer>.Build(playerList, AoiRadius, static player => player.Position);
                var resourceNodes = CreateActiveResourceNodes();
                var resourceNodeIndex = SpatialIndex<ResourceNode>.Build(resourceNodes, AoiRadius, static node => node.Position);
                var mobList = CreateActiveMobs();
                var mobIndex = SpatialIndex<ZoneMob>.Build(mobList, AoiRadius, static mob => mob.Position);

                foreach (var player in playerList)
                {
                    if (player.RemoteEndpoint == null)
                    {
                        continue;
                    }

                    try
                    {
                        var snapshot = BuildSnapshotForPlayer(player, playerIndex, resourceNodeIndex, mobIndex);
                        var payload = WireProtocol.SerializeUdpMessage(snapshot);
                        await _udpClient.SendAsync(payload, payload.Length, player.RemoteEndpoint).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Unity zone " + _definition.ZoneId + " snapshot send failed for player " + player.PlayerId + ": " + ex.Message);
                    }
                }

                foreach (var pair in _players.ToArray())
                {
                    if (pair.Value.PendingDestinationZoneId.HasValue && pair.Value.PendingDestinationZoneId.Value != _definition.ZoneId)
                    {
                        _players.TryRemove(pair.Key, out _);
                    }
                }

                var nowUtc = DateTimeOffset.UtcNow;
                foreach (var pair in _players.ToArray())
                {
                    var lastSeenUtc = pair.Value.LastTcpSeenUtc > pair.Value.LastUdpSeenUtc
                        ? pair.Value.LastTcpSeenUtc
                        : pair.Value.LastUdpSeenUtc;
                    if (pair.Value.DisconnectGraceDeadlineUtc.HasValue)
                    {
                        if (nowUtc < pair.Value.DisconnectGraceDeadlineUtc.Value)
                        {
                            continue;
                        }

                        Debug.LogWarning("Unity zone " + _definition.ZoneId + " finalizing disconnect for session " + pair.Key + " after reconnect grace expired.");
                        _players.TryRemove(pair.Key, out _);
                        continue;
                    }

                    if (nowUtc - lastSeenUtc <= SessionTimeout)
                    {
                        continue;
                    }

                    pair.Value.DisconnectGraceDeadlineUtc = nowUtc.AddSeconds(10);
                    Debug.LogWarning("Unity zone " + _definition.ZoneId + " session " + pair.Key + " for player " + pair.Value.PlayerId + " entered reconnect grace.");
                    try
                    {
                        await SendToPlayerAsync(
                            pair.Value,
                            new DisconnectNoticeMessage("Connection stalled. Waiting for reconnect grace.", true, 10),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogError("Unity zone " + _definition.ZoneId + " tick loop crashed: " + ex);
        }
    }

    private async Task HandleGameplayCommandAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        if (command.SessionId != player.SessionId)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Session mismatch.", string.Empty, 0, player.GetPrimaryGatheringSkill()),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (command.CommandKind)
        {
            case GameplayCommandKind.Gather:
                await HandleGatherAsync(player, command, cancellationToken).ConfigureAwait(false);
                return;
            case GameplayCommandKind.CastSpell:
                await HandleCastSpellAsync(player, command, cancellationToken).ConfigureAwait(false);
                return;
            case GameplayCommandKind.Farming:
                await HandleFarmingAsync(player, command, cancellationToken).ConfigureAwait(false);
                return;
            case GameplayCommandKind.Tame:
                await HandleTameAsync(player, command, cancellationToken).ConfigureAwait(false);
                return;
            case GameplayCommandKind.Craft:
                await HandleCraftAsync(player, command, cancellationToken).ConfigureAwait(false);
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
                        player.GetPrimaryGatheringSkill()),
                    cancellationToken).ConfigureAwait(false);
                return;
            default:
                await SendToPlayerAsync(
                    player,
                    new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unsupported gameplay command.", string.Empty, 0, player.GetPrimaryGatheringSkill()),
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
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Gather on cooldown.", string.Empty, 0, player.GetPrimaryGatheringSkill()),
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
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unknown resource node.", string.Empty, 0, player.GetPrimaryGatheringSkill()),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var distanceSq = DistanceSquared(player.Position, node.Position);
        if (distanceSq > 25f)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Too far away to gather.", string.Empty, 0, player.GetPrimaryGatheringSkill()),
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
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Node is depleted.", node.ItemId, player.GetItemCount(node.ItemId), player.GetPrimaryGatheringSkill()),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var skillKind = ResolveSkillKind(node.ItemId);
        var currentSkill = player.GetSkillValue(skillKind);
        var yieldMultiplier = player.GetGatherYieldMultiplier(DateTimeOffset.UtcNow);
        var amount = Math.Max(1, Mathf.RoundToInt((currentSkill / 8f) * yieldMultiplier));
        player.AddItem(node.ItemId, amount);
        var attempts = player.IncrementGatherAttempts(skillKind);
        if (attempts % 3 == 0)
        {
            player.IncreaseSkill(skillKind, 1);
        }

        var gatherCooldownMultiplier = player.GetGatherCooldownMultiplier(DateTimeOffset.UtcNow);
        var cooldownMs = Mathf.Clamp(Mathf.RoundToInt(750f * gatherCooldownMultiplier), 250, 3000);
        player.NextGatherAllowedUtc = DateTimeOffset.UtcNow.AddMilliseconds(cooldownMs);
        var updatedSkill = player.GetSkillValue(skillKind);
        await SendToPlayerAsync(
            player,
            new GameplayResultMessage(
                command.SessionId,
                command.CommandId,
                true,
                "Gathered " + amount + " " + node.ItemId + " (x" + yieldMultiplier.ToString("F2") + ").",
                node.ItemId,
                player.GetItemCount(node.ItemId),
                updatedSkill),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleFarmingAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var target = (command.TargetId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Use target 'plant:<crop>' or 'harvest'.", string.Empty, 0, player.FarmingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (target.StartsWith("plant:", StringComparison.OrdinalIgnoreCase))
        {
            var cropId = target.Substring("plant:".Length).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(cropId))
            {
                cropId = "wheat";
            }

            FarmPlot existing;
            lock (_farmSync)
            {
                existing = _farmPlots.Values.FirstOrDefault(plot =>
                    plot.OwnerPlayerId == player.PlayerId &&
                    DistanceSquared(plot.Position, player.Position) <= 4f);
                if (existing == null)
                {
                    var plotId = "plot-" + player.PlayerId + "-" + _tick + "-" + _farmPlots.Count;
                    var readyInSeconds = Math.Max(8, 18 - (player.FarmingSkill / 10));
                    _farmPlots[plotId] = new FarmPlot(plotId, player.PlayerId, cropId, player.Position, now.AddSeconds(readyInSeconds));
                }
            }

            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, true, "Planted " + cropId + ".", cropId, player.GetItemCount(cropId), player.FarmingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!target.Equals("harvest", StringComparison.OrdinalIgnoreCase))
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unknown farming action.", string.Empty, 0, player.FarmingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        FarmPlot plotToHarvest = null;
        lock (_farmSync)
        {
            plotToHarvest = _farmPlots.Values.FirstOrDefault(plot =>
                plot.OwnerPlayerId == player.PlayerId &&
                plot.ReadyAtUtc <= now &&
                DistanceSquared(plot.Position, player.Position) <= 9f);
            if (plotToHarvest != null)
            {
                _farmPlots.Remove(plotToHarvest.PlotId);
            }
        }

        if (plotToHarvest == null)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "No ready farm plot nearby.", string.Empty, 0, player.FarmingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var yield = Math.Max(1, Mathf.FloorToInt(player.FarmingSkill / 12f) + 1);
        player.AddItem(plotToHarvest.CropId, yield);
        player.FarmingAttempts++;
        if (player.FarmingAttempts % 2 == 0)
        {
            player.FarmingSkill = Math.Min(100, player.FarmingSkill + 1);
        }

        await SendToPlayerAsync(
            player,
            new GameplayResultMessage(
                command.SessionId,
                command.CommandId,
                true,
                "Harvested " + yield + " " + plotToHarvest.CropId + ".",
                plotToHarvest.CropId,
                player.GetItemCount(plotToHarvest.CropId),
                player.FarmingSkill),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleTameAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        var targetMobId = (command.TargetId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetMobId))
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Target mob id is required for taming.", string.Empty, 0, player.AnimalTamingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        ZoneMob mob;
        lock (_mobSync)
        {
            _mobs.TryGetValue(targetMobId, out mob);
        }

        if (mob == null)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unknown mob.", string.Empty, 0, player.AnimalTamingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (DistanceSquared(player.Position, mob.Position) > 16f)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Too far away to tame this creature.", string.Empty, 0, player.AnimalTamingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        player.AnimalTamingAttempts++;
        var successChance = Mathf.Clamp01(0.15f + (player.AnimalTamingSkill / 150f));
        if (UnityEngine.Random.value > successChance)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Taming failed.", string.Empty, 0, player.AnimalTamingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        lock (_mobSync)
        {
            if (_mobs.TryGetValue(targetMobId, out mob))
            {
                mob.OwnerPlayerId = player.PlayerId;
                mob.State = "Companion";
            }
        }

        if (player.AnimalTamingAttempts % 2 == 0)
        {
            player.AnimalTamingSkill = Math.Min(100, player.AnimalTamingSkill + 1);
        }

        await SendToPlayerAsync(
            player,
            new GameplayResultMessage(
                command.SessionId,
                command.CommandId,
                true,
                "Tamed " + targetMobId + ". It will now follow you.",
                string.Empty,
                0,
                player.AnimalTamingSkill),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCraftAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        var recipeId = (command.TargetId ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Recipe id is required.", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var recipe = CraftRecipe.Resolve(recipeId);
        if (recipe == null)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unknown recipe '" + recipeId + "'.", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!player.TryConsumeItem(recipe.InputItemId, recipe.InputAmount))
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Missing ingredients: " + recipe.InputAmount + " " + recipe.InputItemId + ".", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var bonusOutput = player.CraftingSkill >= 40 && UnityEngine.Random.value < 0.25f ? 1 : 0;
        var totalOutput = recipe.OutputAmount + bonusOutput;
        player.AddItem(recipe.OutputItemId, totalOutput);
        player.CraftingAttempts++;
        if (player.CraftingAttempts % 2 == 0)
        {
            player.CraftingSkill = Math.Min(100, player.CraftingSkill + 1);
        }

        await SendToPlayerAsync(
            player,
            new GameplayResultMessage(
                command.SessionId,
                command.CommandId,
                true,
                "Crafted " + totalOutput + " " + recipe.OutputItemId + ".",
                recipe.OutputItemId,
                player.GetItemCount(recipe.OutputItemId),
                player.CraftingSkill),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCastSpellAsync(ZonePlayer player, GameplayCommandMessage command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var spellId = (command.TargetId ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(spellId))
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Spell id is required.", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var spell = SpellDefinition.Resolve(spellId);
        if (spell == null)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Unknown spell '" + spellId + "'.", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (now < player.NextSpellAllowedUtc)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Spell is on cooldown.", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (player.Mana < spell.ManaCost)
        {
            await SendToPlayerAsync(
                player,
                new GameplayResultMessage(command.SessionId, command.CommandId, false, "Not enough mana.", string.Empty, 0, player.CraftingSkill),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        player.Mana -= spell.ManaCost;
        player.NextSpellAllowedUtc = now.AddSeconds(spell.CooldownSeconds);
        player.ActiveEffects.RemoveAll(effect => effect.EffectType == spell.EffectType);
        player.ActiveEffects.Add(new ActiveSpellEffect(spell.EffectType, now.AddSeconds(spell.DurationSeconds), spell.Power));
        player.CraftingSkill = Math.Min(100, player.CraftingSkill + 1);

        await SendToPlayerAsync(
            player,
            new GameplayResultMessage(
                command.SessionId,
                command.CommandId,
                true,
                "Cast " + spell.DisplayName + " (" + spell.EffectType + "). Mana " + player.Mana + "/" + MaxMana + ".",
                string.Empty,
                0,
                player.CraftingSkill),
            cancellationToken).ConfigureAwait(false);
    }

    private void UpdateMagicEffects(float deltaSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var player in _players.Values)
        {
            player.ActiveEffects.RemoveAll(effect => effect.ExpiresUtc <= now);
            player.ManaRegenAccumulator += deltaSeconds;
            var manaRegenMultiplier = player.GetManaRegenMultiplier(now);
            if (player.ManaRegenAccumulator >= 1f / manaRegenMultiplier)
            {
                var ticks = Mathf.FloorToInt(player.ManaRegenAccumulator * manaRegenMultiplier);
                player.Mana = Math.Min(MaxMana, player.Mana + ticks);
                player.ManaRegenAccumulator = Math.Max(0f, player.ManaRegenAccumulator - (ticks / manaRegenMultiplier));
            }
        }
    }

    private void UpdateFarmPlots()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_farmSync)
        {
            var expired = _farmPlots.Values
                .Where(plot => now - plot.ReadyAtUtc > TimeSpan.FromMinutes(10))
                .Select(plot => plot.PlotId)
                .ToArray();

            foreach (var plotId in expired)
            {
                _farmPlots.Remove(plotId);
            }
        }
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

    private ResourceNodeSnapshot[] CreateResourceNodeSnapshots()
    {
        lock (_resourceSync)
        {
            return _resourceNodes.Values
                .Where(node => node.Remaining > 0)
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)
                .Select(node => new ResourceNodeSnapshot(
                    node.NodeId,
                    node.ItemId,
                    node.Position,
                    node.Remaining,
                    node.MaxAmount))
                .ToArray();
        }
    }

    private ResourceNode[] CreateActiveResourceNodes()
    {
        lock (_resourceSync)
        {
            return _resourceNodes.Values
                .Where(node => node.Remaining > 0)
                .ToArray();
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

    private void UpdateMobs(float deltaSeconds)
    {
        lock (_mobSync)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            foreach (var mob in _mobs.Values)
            {
                if (mob.OwnerPlayerId.HasValue && TryGetPlayerById(mob.OwnerPlayerId.Value, out var owner))
                {
                    mob.TargetPosition = _definition.Clamp(owner.Position);
                    mob.State = "Companion";
                }

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
                        mob.NextDecisionUtc = nowUtc.AddSeconds(NextRandomFloat(2.0f, 5.0f));
                        mob.State = "Wandering";
                    }
                    else
                    {
                        mob.State = "Idle";
                    }

                    continue;
                }

                var distance = Mathf.Sqrt(distanceSq);
                var step = Mathf.Min(distance, mob.Speed * deltaSeconds);
                var nx = toTargetX / Mathf.Max(distance, 0.0001f);
                var nz = toTargetZ / Mathf.Max(distance, 0.0001f);
                mob.Velocity = new NetworkVector3(nx * mob.Speed, 0f, nz * mob.Speed);
                mob.Position = new NetworkVector3(mob.Position.X + nx * step, 0f, mob.Position.Z + nz * step);
                mob.State = "Wandering";
            }
        }
    }

    private NetworkVector3 ChooseMobTarget(ZoneMob mob)
    {
        var x = mob.SpawnPosition.X + NextRandomFloat(-mob.WanderRadius, mob.WanderRadius);
        var z = mob.SpawnPosition.Z + NextRandomFloat(-mob.WanderRadius, mob.WanderRadius);
        return _definition.Clamp(new NetworkVector3(x, 0f, z));
    }

    private float NextRandomFloat(float min, float max)
    {
        lock (_mobSync)
        {
            return (float)(min + (_random.NextDouble() * (max - min)));
        }
    }

    private bool TryGetPlayerById(ulong playerId, out ZonePlayer player)
    {
        foreach (var entry in _players.Values)
        {
            if (entry.PlayerId == playerId)
            {
                player = entry;
                return true;
            }
        }

        player = null;
        return false;
    }

    private MobSnapshot[] CreateMobSnapshots()
    {
        lock (_mobSync)
        {
            return _mobs.Values
                .OrderBy(mob => mob.MobId, StringComparer.Ordinal)
                .Select(mob => new MobSnapshot(mob.MobId, mob.MobTypeId, mob.Position, mob.Velocity, mob.State))
                .ToArray();
        }
    }

    private ZoneMob[] CreateActiveMobs()
    {
        lock (_mobSync)
        {
            return _mobs.Values.ToArray();
        }
    }

    private WorldSnapshotMessage BuildSnapshotForPlayer(
        ZonePlayer recipient,
        SpatialIndex<ZonePlayer> playerIndex,
        SpatialIndex<ResourceNode> resourceNodeIndex,
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

        playerSnapshots.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));

        var visibleNodes = new List<ResourceNodeSnapshot>(4);
        foreach (var node in resourceNodeIndex.EnumerateNearby(recipient.Position))
        {
            if (!IsWithinAoi(recipient.Position, node.Position))
            {
                continue;
            }

            visibleNodes.Add(new ResourceNodeSnapshot(
                node.NodeId,
                node.ItemId,
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

        visibleMobs.Sort((left, right) => string.CompareOrdinal(left.MobId, right.MobId));

        return new WorldSnapshotMessage(
            _definition.ZoneId,
            _tick,
            playerSnapshots.ToArray(),
            visibleNodes.ToArray(),
            visibleMobs.ToArray());
    }

    private async Task ReportMobsAsync(CancellationToken cancellationToken)
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
                        new ZoneMobStateUpdateMessage(_definition.ZoneId, CreateMobSnapshots()),
                        cancellationToken).ConfigureAwait(false);

                    await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Unity zone " + _definition.ZoneId + " failed to report mobs: " + ex.Message);
        }
    }

    private static Dictionary<string, ZoneMob> CreateDefaultMobs(ZoneDefinition definition, int mobCount)
    {
        var mobs = new Dictionary<string, ZoneMob>(StringComparer.OrdinalIgnoreCase);
        if (mobCount <= 0)
        {
            return mobs;
        }

        var width = Mathf.Max(definition.MaxX - definition.MinX - 12f, 1f);
        var depth = Mathf.Max(definition.MaxZ - definition.MinZ - 12f, 1f);
        var columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(mobCount)));
        var rows = Mathf.Max(1, Mathf.CeilToInt(mobCount / (float)columns));
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

    private static float DistanceSquared(NetworkVector3 a, NetworkVector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    private static bool IsWithinAoi(NetworkVector3 origin, NetworkVector3 target)
    {
        return DistanceSquared(origin, target) <= AoiRadiusSquared;
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

        var planarMagnitude = Mathf.Sqrt(planarMagnitudeSq);
        if (planarMagnitude <= 0.0001f)
        {
            return NetworkVector3.Zero;
        }

        var scale = MaxMoveMagnitude / planarMagnitude;
        return new NetworkVector3(move.X * scale, 0f, move.Z * scale);
    }

    private static string BuildInventorySummary(ZonePlayer player)
    {
        var logCount = player.GetItemCount("log");
        var oreCount = player.GetItemCount("ore");
        var wheatCount = player.GetItemCount("wheat");
        return "Inventory -> log: " + logCount + ", ore: " + oreCount + ", wheat: " + wheatCount
            + " | Woodcutting " + player.WoodcuttingSkill
            + " | Mining " + player.MiningSkill
            + " | Crafting " + player.CraftingSkill
            + " | Farming " + player.FarmingSkill
            + " | Taming " + player.AnimalTamingSkill
            + " | Mana " + player.Mana + "/" + MaxMana
            + " | Effects " + player.ActiveEffects.Count;
    }

    private static SkillKind ResolveSkillKind(string itemId)
    {
        return itemId switch
        {
            "ore" => SkillKind.Mining,
            "log" => SkillKind.Woodcutting,
            _ => SkillKind.Gathering
        };
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

    private async Task ReportStatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var updates = _players.Values
                .Select(player => new ZonePlayerStateUpdate(player.SessionId, player.PlayerId, player.Position, player.Velocity))
                .ToArray();

            if (updates.Length == 0)
            {
                return;
            }

            using (var client = new TcpClient())
            {
                await client.ConnectAsync(_controlHost, _controlPort).ConfigureAwait(false);
                using (var stream = client.GetStream())
                {
                    await WireProtocol.WriteTcpMessageAsync(
                        stream,
                        new ZoneStateBatchUpdateMessage(_definition.ZoneId, updates),
                        cancellationToken).ConfigureAwait(false);
                    await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Zone state batch update failed: " + ex.Message);
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
            var candidate = _definition.ZoneId + 1;
            return candidate > 0 ? candidate : null;
        }

        if (position.X - _definition.MinX <= margin)
        {
            var candidate = _definition.ZoneId - 1;
            return candidate > 0 ? candidate : null;
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
            LastTcpSeenUtc = DateTimeOffset.UtcNow;
            LastUdpSeenUtc = DateTimeOffset.UtcNow;
            WoodcuttingSkill = 1;
            MiningSkill = 1;
            CraftingSkill = 1;
            FarmingSkill = 1;
            AnimalTamingSkill = 1;
            Mana = MaxMana;
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
        public int WoodcuttingSkill { get; set; }
        public int MiningSkill { get; set; }
        public int CraftingSkill { get; set; }
        public int FarmingSkill { get; set; }
        public int AnimalTamingSkill { get; set; }
        public int CraftingAttempts { get; set; }
        public int WoodcuttingAttempts { get; set; }
        public int MiningAttempts { get; set; }
        public int GatheringAttempts { get; set; }
        public int FarmingAttempts { get; set; }
        public int AnimalTamingAttempts { get; set; }
        public int Mana { get; set; }
        public float ManaRegenAccumulator { get; set; }
        public DateTimeOffset NextSpellAllowedUtc { get; set; }
        public DateTimeOffset NextGatherAllowedUtc { get; set; }
        public DateTimeOffset LastTcpSeenUtc { get; set; }
        public DateTimeOffset LastUdpSeenUtc { get; set; }
        public DateTimeOffset? DisconnectGraceDeadlineUtc { get; set; }
        public uint LastAcceptedInputSequence { get; set; }
        public SemaphoreSlim ControlWriteLock { get; } = new SemaphoreSlim(1, 1);
        public List<ActiveSpellEffect> ActiveEffects { get; } = new List<ActiveSpellEffect>();

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

        public bool TryConsumeItem(string itemId, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            int existing;
            if (!_inventory.TryGetValue(itemId, out existing) || existing < amount)
            {
                return false;
            }

            var next = existing - amount;
            if (next <= 0)
            {
                _inventory.Remove(itemId);
            }
            else
            {
                _inventory[itemId] = next;
            }

            return true;
        }

        public int GetPrimaryGatheringSkill()
        {
            return Math.Max(WoodcuttingSkill, MiningSkill);
        }

        public int GetSkillValue(SkillKind skillKind)
        {
            return skillKind switch
            {
                SkillKind.Woodcutting => WoodcuttingSkill,
                SkillKind.Mining => MiningSkill,
                SkillKind.Crafting => CraftingSkill,
                _ => GetPrimaryGatheringSkill()
            };
        }

        public int IncrementGatherAttempts(SkillKind skillKind)
        {
            switch (skillKind)
            {
                case SkillKind.Woodcutting:
                    WoodcuttingAttempts++;
                    return WoodcuttingAttempts;
                case SkillKind.Mining:
                    MiningAttempts++;
                    return MiningAttempts;
                default:
                    GatheringAttempts++;
                    return GatheringAttempts;
            }
        }

        public void IncreaseSkill(SkillKind skillKind, int amount)
        {
            amount = Math.Max(amount, 0);
            switch (skillKind)
            {
                case SkillKind.Woodcutting:
                    WoodcuttingSkill += amount;
                    break;
                case SkillKind.Mining:
                    MiningSkill += amount;
                    break;
                case SkillKind.Crafting:
                    CraftingSkill += amount;
                    break;
                default:
                    WoodcuttingSkill += amount;
                    MiningSkill += amount;
                    break;
            }
        }

        public float GetMovementSpeedMultiplier(DateTimeOffset now)
        {
            var speedMultiplier = 1f;
            foreach (var effect in ActiveEffects)
            {
                if (effect.ExpiresUtc <= now)
                {
                    continue;
                }

                if (effect.EffectType == SpellEffectType.Haste)
                {
                    speedMultiplier += effect.Power;
                }
            }

            return Mathf.Clamp(speedMultiplier, 0.2f, 2.5f);
        }

        public float GetManaRegenMultiplier(DateTimeOffset now)
        {
            var value = 1f;
            foreach (var effect in ActiveEffects)
            {
                if (effect.ExpiresUtc > now && effect.EffectType == SpellEffectType.Rejuvenation)
                {
                    value += effect.Power;
                }
            }

            return Mathf.Clamp(value, 0.25f, 4f);
        }

        public float GetGatherYieldMultiplier(DateTimeOffset now)
        {
            var value = 1f;
            foreach (var effect in ActiveEffects)
            {
                if (effect.ExpiresUtc > now && effect.EffectType == SpellEffectType.StoneSkin)
                {
                    value += effect.Power;
                }
            }

            return Mathf.Clamp(value, 0.25f, 3f);
        }

        public float GetGatherCooldownMultiplier(DateTimeOffset now)
        {
            var value = 1f;
            foreach (var effect in ActiveEffects)
            {
                if (effect.ExpiresUtc > now && effect.EffectType == SpellEffectType.StoneSkin)
                {
                    value -= (effect.Power * 0.35f);
                }
            }

            return Mathf.Clamp(value, 0.35f, 2.5f);
        }
    }

    private enum SkillKind
    {
        Gathering = 0,
        Woodcutting = 1,
        Mining = 2,
        Crafting = 3
    }

    private sealed class CraftRecipe
    {
        private CraftRecipe(string recipeId, string inputItemId, int inputAmount, string outputItemId, int outputAmount)
        {
            RecipeId = recipeId;
            InputItemId = inputItemId;
            InputAmount = inputAmount;
            OutputItemId = outputItemId;
            OutputAmount = outputAmount;
        }

        public string RecipeId { get; }
        public string InputItemId { get; }
        public int InputAmount { get; }
        public string OutputItemId { get; }
        public int OutputAmount { get; }

        public static CraftRecipe Resolve(string recipeId)
        {
            return recipeId switch
            {
                "plank" => new CraftRecipe("plank", "log", 2, "plank", 1),
                "ingot" => new CraftRecipe("ingot", "ore", 2, "ingot", 1),
                "flour" => new CraftRecipe("flour", "wheat", 2, "flour", 1),
                _ => null
            };
        }
    }

    private sealed class FarmPlot
    {
        public FarmPlot(string plotId, ulong ownerPlayerId, string cropId, NetworkVector3 position, DateTimeOffset readyAtUtc)
        {
            PlotId = plotId;
            OwnerPlayerId = ownerPlayerId;
            CropId = cropId;
            Position = position;
            ReadyAtUtc = readyAtUtc;
        }

        public string PlotId { get; }
        public ulong OwnerPlayerId { get; }
        public string CropId { get; }
        public NetworkVector3 Position { get; }
        public DateTimeOffset ReadyAtUtc { get; }
    }

    private enum SpellEffectType
    {
        Haste = 1,
        StoneSkin = 2,
        Rejuvenation = 3
    }

    private sealed class ActiveSpellEffect
    {
        public ActiveSpellEffect(SpellEffectType effectType, DateTimeOffset expiresUtc, float power)
        {
            EffectType = effectType;
            ExpiresUtc = expiresUtc;
            Power = power;
        }

        public SpellEffectType EffectType { get; }
        public DateTimeOffset ExpiresUtc { get; }
        public float Power { get; }
    }

    private sealed class SpellDefinition
    {
        private SpellDefinition(string spellId, string displayName, SpellEffectType effectType, int manaCost, float durationSeconds, float cooldownSeconds, float power)
        {
            SpellId = spellId;
            DisplayName = displayName;
            EffectType = effectType;
            ManaCost = manaCost;
            DurationSeconds = durationSeconds;
            CooldownSeconds = cooldownSeconds;
            Power = power;
        }

        public string SpellId { get; }
        public string DisplayName { get; }
        public SpellEffectType EffectType { get; }
        public int ManaCost { get; }
        public float DurationSeconds { get; }
        public float CooldownSeconds { get; }
        public float Power { get; }

        public static SpellDefinition Resolve(string spellId)
        {
            return spellId switch
            {
                "haste" => new SpellDefinition("haste", "Haste", SpellEffectType.Haste, 20, 12f, 8f, 0.35f),
                "stoneskin" => new SpellDefinition("stoneskin", "Stone Skin", SpellEffectType.StoneSkin, 30, 18f, 10f, 0.25f),
                "rejuvenation" => new SpellDefinition("rejuvenation", "Rejuvenation", SpellEffectType.Rejuvenation, 25, 10f, 10f, 0.5f),
                _ => null
            };
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
            NextDecisionUtc = DateTimeOffset.UtcNow.AddSeconds(1.5);
        }

        public string MobId { get; private set; }
        public string MobTypeId { get; private set; }
        public NetworkVector3 SpawnPosition { get; private set; }
        public NetworkVector3 Position { get; set; }
        public NetworkVector3 Velocity { get; set; }
        public NetworkVector3 TargetPosition { get; set; }
        public DateTimeOffset NextDecisionUtc { get; set; }
        public float Speed { get; private set; }
        public float WanderRadius { get; private set; }
        public string State { get; set; }
        public ulong? OwnerPlayerId { get; set; }
    }

    private struct CellKey : IEquatable<CellKey>
    {
        public CellKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public bool Equals(CellKey other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is CellKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Z;
            }
        }
    }

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
                    Mathf.FloorToInt(position.X / cellSize),
                    Mathf.FloorToInt(position.Z / cellSize));

                List<T> bucket;
                if (!buckets.TryGetValue(key, out bucket))
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
            var cellX = Mathf.FloorToInt(origin.X / _cellSize);
            var cellZ = Mathf.FloorToInt(origin.Z / _cellSize);

            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    List<T> bucket;
                    if (_buckets.TryGetValue(new CellKey(cellX + dx, cellZ + dz), out bucket))
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
}
