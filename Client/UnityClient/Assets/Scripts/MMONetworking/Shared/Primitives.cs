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

    public static NetworkVector3 Zero => new NetworkVector3(0f, 0f, 0f);

    public static NetworkVector3 operator +(NetworkVector3 left, NetworkVector3 right)
        => new NetworkVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static NetworkVector3 operator *(NetworkVector3 value, float scalar)
        => new NetworkVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
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
}
