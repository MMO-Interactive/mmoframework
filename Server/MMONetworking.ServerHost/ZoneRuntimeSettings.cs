using System;

namespace MMONetworking.ServerHost;

public sealed record ZoneRuntimeSettings(
    float AoiRadius,
    float GhostMargin,
    float PrewarmMargin,
    TimeSpan GhostTtl,
    float TransferInset,
    int DefaultMobCountPerZone,
    int Zone1MobCount,
    TimeSpan HeartbeatInterval,
    TimeSpan SessionTimeout,
    TimeSpan ReconnectGracePeriod,
    TimeSpan IdleShutdownDelay,
    int ZoneControlPort,
    bool UseUnityZoneProcess,
    string UnityZoneExecutablePath)
{
    public static ZoneRuntimeSettings Load(string workspaceRoot)
        => new(
            AoiRadius: ReadFloat("MMO_AOI_RADIUS", 36f),
            GhostMargin: ReadFloat("MMO_GHOST_MARGIN", 14f),
            PrewarmMargin: ReadFloat("MMO_PREWARM_MARGIN", 20f),
            GhostTtl: TimeSpan.FromMilliseconds(ReadInt("MMO_GHOST_TTL_MS", 1800)),
            TransferInset: ReadFloat("MMO_TRANSFER_INSET", 4f),
            DefaultMobCountPerZone: ReadInt("MMO_DEFAULT_MOB_COUNT", 2),
            Zone1MobCount: ReadInt("MMO_ZONE1_MOB_COUNT", 100),
            HeartbeatInterval: TimeSpan.FromSeconds(ReadInt("MMO_HEARTBEAT_SECONDS", 2)),
            SessionTimeout: TimeSpan.FromSeconds(ReadInt("MMO_SESSION_TIMEOUT_SECONDS", 12)),
            ReconnectGracePeriod: TimeSpan.FromSeconds(ReadInt("MMO_RECONNECT_GRACE_SECONDS", 10)),
            IdleShutdownDelay: TimeSpan.FromSeconds(ReadInt("MMO_IDLE_SHUTDOWN_SECONDS", 120)),
            ZoneControlPort: ReadInt("MMO_ZONE_CONTROL_PORT", 7050),
            UseUnityZoneProcess: ReadBool("MMO_USE_UNITY_ZONE_PROCESS", false),
            UnityZoneExecutablePath: Environment.GetEnvironmentVariable("MMO_UNITY_ZONE_EXE")
                ?? System.IO.Path.Combine(workspaceRoot, "Builds", "ZoneServer", "Win64", "ZoneServer.exe"));

    public int GetMobCountForZone(int zoneId)
        => zoneId == 1 ? Math.Max(0, Zone1MobCount) : Math.Max(0, DefaultMobCountPerZone);

    private static float ReadFloat(string name, float fallback)
        => float.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int ReadInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static bool ReadBool(string name, bool fallback)
        => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}
