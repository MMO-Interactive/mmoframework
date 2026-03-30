using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMONetworking.ServerHost;

public interface IZoneRuntimeHost : IDisposable
{
    int ActivePlayerCount { get; }
    Task Ready { get; }
    string RuntimeMode { get; }
    Task RunAsync(CancellationToken cancellationToken);
    ZoneRuntimeSnapshot CreateDashboardSnapshot(string lifecycleState, DateTimeOffset lastStateChangeUtc, DateTimeOffset? idleSinceUtc);
    void ReportPlayerState(Guid sessionId, ulong playerId, NetworkVector3 position, NetworkVector3 velocity);
    void RemovePlayer(Guid sessionId);
}
