using System;

namespace MMONetworking
{
public readonly struct NetworkVector3
{
    public NetworkVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public static NetworkVector3 Zero => new(0f, 0f, 0f);

    public static NetworkVector3 operator +(NetworkVector3 left, NetworkVector3 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static NetworkVector3 operator *(NetworkVector3 value, float scalar)
        => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
}

public enum SnapshotEntityKind : byte
{
    Player = 0,
    Ghost = 1
}

public readonly struct PlayerSnapshot
{
    public PlayerSnapshot(ulong playerId, NetworkVector3 position, NetworkVector3 velocity, SnapshotEntityKind kind, int sourceZoneId)
    {
        PlayerId = playerId;
        Position = position;
        Velocity = velocity;
        Kind = kind;
        SourceZoneId = sourceZoneId;
    }

    public ulong PlayerId { get; }
    public NetworkVector3 Position { get; }
    public NetworkVector3 Velocity { get; }
    public SnapshotEntityKind Kind { get; }
    public int SourceZoneId { get; }
}

public readonly struct ResourceNodeSnapshot
{
    public ResourceNodeSnapshot(string nodeId, string resourceId, NetworkVector3 position, int remaining, int maxAmount)
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

public readonly struct MobSnapshot
{
    public MobSnapshot(string mobId, string mobTypeId, NetworkVector3 position, NetworkVector3 velocity, string state)
    {
        MobId = mobId;
        MobTypeId = mobTypeId;
        Position = position;
        Velocity = velocity;
        State = state;
    }

    public string MobId { get; }
    public string MobTypeId { get; }
    public NetworkVector3 Position { get; }
    public NetworkVector3 Velocity { get; }
    public string State { get; }
}

public readonly struct ZonePlayerStateUpdate
{
    public ZonePlayerStateUpdate(Guid sessionId, ulong playerId, NetworkVector3 position, NetworkVector3 velocity)
    {
        SessionId = sessionId;
        PlayerId = playerId;
        Position = position;
        Velocity = velocity;
    }

    public Guid SessionId { get; }
    public ulong PlayerId { get; }
    public NetworkVector3 Position { get; }
    public NetworkVector3 Velocity { get; }
}

public readonly struct ZoneDefinition
{
    public ZoneDefinition(int zoneId, string name, string host, int tcpPort, int udpPort, float minX, float maxX, float minZ, float maxZ)
    {
        ZoneId = zoneId;
        Name = name;
        Host = host;
        TcpPort = tcpPort;
        UdpPort = udpPort;
        MinX = minX;
        MaxX = maxX;
        MinZ = minZ;
        MaxZ = maxZ;
    }

    public int ZoneId { get; }
    public string Name { get; }
    public string Host { get; }
    public int TcpPort { get; }
    public int UdpPort { get; }
    public float MinX { get; }
    public float MaxX { get; }
    public float MinZ { get; }
    public float MaxZ { get; }

    public bool Contains(NetworkVector3 position)
        => position.X >= MinX && position.X < MaxX && position.Z >= MinZ && position.Z < MaxZ;

    public NetworkVector3 Clamp(NetworkVector3 position)
    {
        var x = Math.Max(MinX + 0.25f, Math.Min(MaxX - 0.25f, position.X));
        var z = Math.Max(MinZ + 0.25f, Math.Min(MaxZ - 0.25f, position.Z));
        return new NetworkVector3(x, position.Y, z);
    }
}
}
