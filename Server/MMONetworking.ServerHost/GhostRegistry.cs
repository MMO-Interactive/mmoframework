using System;
using System.Collections.Concurrent;
using System.Linq;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class GhostRegistry
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, GhostReplica>> _ghostsByZone = new();

    public void Publish(
        int zoneId,
        Guid sessionId,
        ulong playerId,
        int sourceZoneId,
        NetworkVector3 position,
        NetworkVector3 velocity,
        TimeSpan ttl)
    {
        var zoneGhosts = _ghostsByZone.GetOrAdd(zoneId, _ => new ConcurrentDictionary<Guid, GhostReplica>());
        zoneGhosts[sessionId] = new GhostReplica(
            sessionId,
            playerId,
            sourceZoneId,
            position,
            velocity,
            DateTimeOffset.UtcNow.Add(ttl));
    }

    public GhostReplica[] GetActiveGhosts(int zoneId, DateTimeOffset nowUtc)
    {
        if (!_ghostsByZone.TryGetValue(zoneId, out var zoneGhosts))
        {
            return Array.Empty<GhostReplica>();
        }

        foreach (var pair in zoneGhosts.ToArray())
        {
            if (pair.Value.ExpiresAtUtc <= nowUtc)
            {
                zoneGhosts.TryRemove(pair.Key, out _);
            }
        }

        return zoneGhosts.Values
            .Where(ghost => ghost.ExpiresAtUtc > nowUtc)
            .OrderBy(ghost => ghost.PlayerId)
            .ToArray();
    }

    public int GetActiveGhostCount(int zoneId, DateTimeOffset nowUtc)
        => GetActiveGhosts(zoneId, nowUtc).Length;

    public sealed record GhostReplica(
        Guid SessionId,
        ulong PlayerId,
        int SourceZoneId,
        NetworkVector3 Position,
        NetworkVector3 Velocity,
        DateTimeOffset ExpiresAtUtc);
}
