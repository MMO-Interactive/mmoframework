using System;

namespace MMONetworking;

public readonly record struct NetworkVector3(float X, float Y, float Z)
{
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

public readonly record struct PlayerSnapshot(
    ulong PlayerId,
    NetworkVector3 Position,
    NetworkVector3 Velocity,
    SnapshotEntityKind Kind,
    int SourceZoneId);

public readonly record struct ResourceNodeSnapshot(
    string NodeId,
    string ResourceId,
    NetworkVector3 Position,
    int Remaining,
    int MaxAmount);

public readonly record struct MobSnapshot(
    string MobId,
    string MobTypeId,
    NetworkVector3 Position,
    NetworkVector3 Velocity,
    string State,
    int HitPoints,
    int MaxHitPoints);

public readonly record struct NpcServiceSnapshot(
    string ActionId,
    string Label,
    string UiHint);

public readonly record struct NpcSnapshot(
    string NpcId,
    string NpcTypeId,
    string DisplayName,
    NetworkVector3 Position,
    string PrimaryRole,
    string[] Services,
    string GreetingText,
    NpcServiceSnapshot[] ServiceOptions);

public readonly record struct ZonePlayerStateUpdate(
    Guid SessionId,
    ulong PlayerId,
    NetworkVector3 Position,
    NetworkVector3 Velocity);

public readonly record struct ZoneDefinition(
    int ZoneId,
    string Name,
    string Host,
    int TcpPort,
    int UdpPort,
    float MinX,
    float MaxX,
    float MinZ,
    float MaxZ,
    string AssetBundleName = "")
{
    public const float StandardZoneSizeMeters = 2000f;

    public int GridX => (int)MathF.Round(MinX / StandardZoneSizeMeters);
    public int GridZ => (int)MathF.Round(MinZ / StandardZoneSizeMeters);

    public bool Contains(NetworkVector3 position)
        => position.X >= MinX && position.X < MaxX && position.Z >= MinZ && position.Z < MaxZ;

    public NetworkVector3 Clamp(NetworkVector3 position)
    {
        var x = Math.Clamp(position.X, MinX + 0.25f, MaxX - 0.25f);
        var z = Math.Clamp(position.Z, MinZ + 0.25f, MaxZ - 0.25f);
        return new NetworkVector3(x, position.Y, z);
    }
}
