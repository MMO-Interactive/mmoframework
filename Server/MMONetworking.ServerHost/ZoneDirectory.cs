using System;
using System.Collections.Generic;
using System.Linq;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class ZoneDirectory
{
    private readonly IReadOnlyDictionary<int, ZoneDefinition> _zones;

    public ZoneDirectory(IEnumerable<ZoneDefinition> zones)
    {
        _zones = zones.ToDictionary(zone => zone.ZoneId);
    }

    public ZoneDefinition GetZone(int zoneId)
        => _zones[zoneId];

    public bool HasZone(int zoneId)
        => _zones.ContainsKey(zoneId);

    public IReadOnlyCollection<ZoneDefinition> All => _zones.Values.ToArray();

    public ZoneDefinition ResolveDestination(NetworkVector3 position, int currentZoneId)
    {
        if (_zones[currentZoneId].Contains(position))
        {
            return _zones[currentZoneId];
        }

        foreach (var zone in _zones.Values)
        {
            if (zone.Contains(position))
            {
                return zone;
            }
        }

        return _zones[currentZoneId];
    }

    public ZoneDefinition[] GetGhostDestinations(int currentZoneId, NetworkVector3 position, float margin)
    {
        var current = _zones[currentZoneId];
        return _zones.Values
            .Where(zone => zone.ZoneId != currentZoneId)
            .Where(zone => SharesBoundary(current, zone) && IsWithinGhostMargin(current, zone, position, margin))
            .ToArray();
    }

    public ZoneDefinition? GetPrewarmDestination(int currentZoneId, NetworkVector3 position, float margin)
        => GetGhostDestinations(currentZoneId, position, margin).OrderBy(zone => zone.ZoneId).FirstOrDefault();

    private static bool SharesBoundary(ZoneDefinition current, ZoneDefinition other)
    {
        var overlapsZ = current.MinZ < other.MaxZ && current.MaxZ > other.MinZ;
        var overlapsX = current.MinX < other.MaxX && current.MaxX > other.MinX;

        var touchEastWest = overlapsZ && (Math.Abs(current.MaxX - other.MinX) < 0.001f || Math.Abs(other.MaxX - current.MinX) < 0.001f);
        var touchNorthSouth = overlapsX && (Math.Abs(current.MaxZ - other.MinZ) < 0.001f || Math.Abs(other.MaxZ - current.MinZ) < 0.001f);
        return touchEastWest || touchNorthSouth;
    }

    private static bool IsWithinGhostMargin(ZoneDefinition current, ZoneDefinition other, NetworkVector3 position, float margin)
    {
        if (Math.Abs(current.MaxX - other.MinX) < 0.001f)
        {
            return current.MaxX - position.X <= margin;
        }

        if (Math.Abs(other.MaxX - current.MinX) < 0.001f)
        {
            return position.X - current.MinX <= margin;
        }

        if (Math.Abs(current.MaxZ - other.MinZ) < 0.001f)
        {
            return current.MaxZ - position.Z <= margin;
        }

        if (Math.Abs(other.MaxZ - current.MinZ) < 0.001f)
        {
            return position.Z - current.MinZ <= margin;
        }

        return false;
    }
}
