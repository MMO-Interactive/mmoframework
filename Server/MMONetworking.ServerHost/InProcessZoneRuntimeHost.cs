using System;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class InProcessZoneRuntimeHost : IZoneRuntimeHost
{
    private readonly ZoneHost _zoneHost;

    public InProcessZoneRuntimeHost(
        ZoneDefinition definition,
        ZoneDirectory zoneDirectory,
        SessionRegistry sessionRegistry,
        GhostRegistry ghostRegistry,
        ZoneRuntimeSettings settings,
        Func<int, CancellationToken, Task> ensureZoneRunningAsync)
    {
        _zoneHost = new ZoneHost(definition, zoneDirectory, sessionRegistry, ghostRegistry, settings, ensureZoneRunningAsync);
    }

    public int ActivePlayerCount => _zoneHost.ActivePlayerCount;
    public Task Ready => _zoneHost.Ready;
    public string RuntimeMode => "InProcess";

    public Task RunAsync(CancellationToken cancellationToken)
        => _zoneHost.RunAsync(cancellationToken);

    public ZoneRuntimeSnapshot CreateDashboardSnapshot(string lifecycleState, DateTimeOffset lastStateChangeUtc, DateTimeOffset? idleSinceUtc)
        => _zoneHost.CreateDashboardSnapshot(lifecycleState, lastStateChangeUtc, idleSinceUtc);

    public void ReportPlayerState(Guid sessionId, ulong playerId, NetworkVector3 position, NetworkVector3 velocity)
    {
    }

    public void RemovePlayer(Guid sessionId)
    {
    }

    public void Dispose()
        => _zoneHost.Dispose();
}
