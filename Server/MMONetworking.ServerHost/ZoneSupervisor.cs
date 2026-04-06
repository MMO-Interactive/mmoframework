using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class ZoneSupervisor
{
    private readonly ZoneDirectory _zoneDirectory;
    private readonly SessionRegistry _sessionRegistry;
    private readonly GhostRegistry _ghostRegistry;
    private readonly ZoneRuntimeSettings _settings;
    private readonly GameplayNetworkService _gameplayNetworkService;
    private readonly GameplayDefinitionStore _gameplayDefinitions;
    private readonly ConcurrentDictionary<int, ManagedZone> _zones = new();

    public ZoneSupervisor(ZoneDirectory zoneDirectory, SessionRegistry sessionRegistry, GhostRegistry ghostRegistry, ZoneRuntimeSettings settings, GameplayNetworkService gameplayNetworkService, GameplayDefinitionStore gameplayDefinitions)
    {
        _zoneDirectory = zoneDirectory;
        _sessionRegistry = sessionRegistry;
        _ghostRegistry = ghostRegistry;
        _settings = settings;
        _gameplayNetworkService = gameplayNetworkService;
        _gameplayDefinitions = gameplayDefinitions;

        foreach (var zone in zoneDirectory.All)
        {
            _zones[zone.ZoneId] = new ManagedZone(zone, EnsureRunningAsync, ghostRegistry, settings, gameplayNetworkService, gameplayDefinitions);
        }
    }

    public Task EnsureRunningAsync(int zoneId, CancellationToken cancellationToken = default)
        => _zones[zoneId].EnsureRunningAsync(_zoneDirectory, _sessionRegistry, cancellationToken);

    public void ReportExternalPlayerState(int zoneId, Guid sessionId, ulong playerId, NetworkVector3 position, NetworkVector3 velocity)
    {
        if (_zones.TryGetValue(zoneId, out var zone))
        {
            zone.ReportPlayerState(sessionId, playerId, position, velocity);
        }
    }

    public void RemoveExternalPlayer(int zoneId, Guid sessionId)
    {
        if (_zones.TryGetValue(zoneId, out var zone))
        {
            zone.RemovePlayer(sessionId);
        }
    }

    public void ReportExternalMobStates(int zoneId, MobSnapshot[] mobs)
    {
        if (_zones.TryGetValue(zoneId, out var zone))
        {
            zone.ReportMobStates(mobs);
        }
    }

    public ZoneRuntimeSnapshot[] CreateDashboardSnapshot()
        => _zones.Values
            .OrderBy(zone => zone.Definition.ZoneId)
            .Select(zone => zone.CreateSnapshot())
            .ToArray();

    public async Task RunLifecycleLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var zone in _zones.Values)
            {
                await zone.EvaluateIdleShutdownAsync(_settings.IdleShutdownDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class ManagedZone
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Func<int, CancellationToken, Task> _ensureZoneRunningAsync;
        private readonly GhostRegistry _ghostRegistry;
        private readonly ZoneRuntimeSettings _settings;
        private readonly GameplayNetworkService _gameplayNetworkService;
        private readonly GameplayDefinitionStore _gameplayDefinitions;
        private IZoneRuntimeHost? _host;
        private CancellationTokenSource? _runCancellation;
        private Task? _runTask;

        public ManagedZone(ZoneDefinition definition, Func<int, CancellationToken, Task> ensureZoneRunningAsync, GhostRegistry ghostRegistry, ZoneRuntimeSettings settings, GameplayNetworkService gameplayNetworkService, GameplayDefinitionStore gameplayDefinitions)
        {
            Definition = definition;
            _ensureZoneRunningAsync = ensureZoneRunningAsync;
            _ghostRegistry = ghostRegistry;
            _settings = settings;
            _gameplayNetworkService = gameplayNetworkService;
            _gameplayDefinitions = gameplayDefinitions;
            LifecycleState = "Stopped";
            LastStateChangeUtc = DateTimeOffset.UtcNow;
        }

        public ZoneDefinition Definition { get; }
        public string LifecycleState { get; private set; }
        public DateTimeOffset LastStateChangeUtc { get; private set; }
        public DateTimeOffset? IdleSinceUtc { get; private set; }

        public async Task EnsureRunningAsync(ZoneDirectory zoneDirectory, SessionRegistry sessionRegistry, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_host is not null)
                {
                    LifecycleState = "Running";
                    if (_host.ActivePlayerCount > 0)
                    {
                        IdleSinceUtc = null;
                    }

                    return;
                }

                LifecycleState = "Starting";
                LastStateChangeUtc = DateTimeOffset.UtcNow;

                _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _host = CreateRuntimeHost(zoneDirectory, sessionRegistry);
                _runTask = Task.Run(async () =>
                {
                    try
                    {
                        await _host.RunAsync(_runCancellation.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await _gate.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            _host?.Dispose();
                            _host = null;
                            _runCancellation?.Dispose();
                            _runCancellation = null;
                            _runTask = null;
                            LifecycleState = "Stopped";
                            LastStateChangeUtc = DateTimeOffset.UtcNow;
                            IdleSinceUtc = null;
                        }
                        finally
                        {
                            _gate.Release();
                        }
                    }
                }, CancellationToken.None);

                await _host.Ready.ConfigureAwait(false);

                LifecycleState = "Running";
                LastStateChangeUtc = DateTimeOffset.UtcNow;
                IdleSinceUtc = DateTimeOffset.UtcNow;
                Console.WriteLine($"Zone {Definition.ZoneId} started on demand.");
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task EvaluateIdleShutdownAsync(TimeSpan idleShutdownDelay, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_host is null)
                {
                    return;
                }

                if (_host.ActivePlayerCount > 0)
                {
                    IdleSinceUtc = null;
                    return;
                }

                IdleSinceUtc ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - IdleSinceUtc < idleShutdownDelay)
                {
                    return;
                }

                LifecycleState = "Stopping";
                LastStateChangeUtc = DateTimeOffset.UtcNow;
                Console.WriteLine($"Zone {Definition.ZoneId} idle timeout reached. Stopping zone.");
                _runCancellation?.Cancel();
                _host.Dispose();
            }
            finally
            {
                _gate.Release();
            }
        }

        public ZoneRuntimeSnapshot CreateSnapshot()
        {
            if (_host is null)
            {
                return new ZoneRuntimeSnapshot(
                    Definition.ZoneId,
                    Definition.Name,
                    Definition.TcpPort,
                    Definition.UdpPort,
                    Definition.MinX,
                    Definition.MaxX,
                    Definition.MinZ,
                    Definition.MaxZ,
                    _settings.UseUnityZoneProcess ? "UnityProcess" : "InProcess",
                    LifecycleState,
                    LastStateChangeUtc,
                    IdleSinceUtc,
                    0,
                    0,
                    0,
                    0,
                    _settings.AoiRadius,
                    _settings.GhostMargin,
                    _settings.PrewarmMargin,
                    _settings.TransferInset,
                    Array.Empty<ZonePlayerSnapshot>(),
                    Array.Empty<ZoneMobSnapshot>());
            }

            return _host.CreateDashboardSnapshot(LifecycleState, LastStateChangeUtc, IdleSinceUtc);
        }

        private IZoneRuntimeHost CreateRuntimeHost(ZoneDirectory zoneDirectory, SessionRegistry sessionRegistry)
        {
            var npcDefinitions = _gameplayDefinitions.GetNpcsForZone(Definition.ZoneId);
            var mobSpawnDefinitions = _gameplayDefinitions.GetMobSpawnsForZone(Definition.ZoneId);
            if (_settings.UseUnityZoneProcess)
            {
                return new ExternalUnityZoneProcessHost(Definition, _settings, npcDefinitions, mobSpawnDefinitions);
            }

            return new InProcessZoneRuntimeHost(Definition, zoneDirectory, sessionRegistry, _ghostRegistry, _settings, _ensureZoneRunningAsync, _gameplayNetworkService, npcDefinitions, mobSpawnDefinitions);
        }

        public void ReportPlayerState(Guid sessionId, ulong playerId, NetworkVector3 position, NetworkVector3 velocity)
        {
            _host?.ReportPlayerState(sessionId, playerId, position, velocity);
            if (_host is not null)
            {
                IdleSinceUtc = null;
            }
        }

        public void RemovePlayer(Guid sessionId)
            => _host?.RemovePlayer(sessionId);

        public void ReportMobStates(MobSnapshot[] mobs)
        {
            _host?.ReportMobStates(mobs);
            if (_host is not null)
            {
                IdleSinceUtc = null;
            }
        }
    }
}
