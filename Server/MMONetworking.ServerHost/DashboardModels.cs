using System;

namespace MMONetworking.ServerHost;

public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAtUtc,
    GatewaySnapshot Gateway,
    SessionSnapshot[] Sessions,
    ZoneRuntimeSnapshot[] Zones);

public sealed record GatewaySnapshot(
    int Port,
    long ConnectionAttempts,
    long SuccessfulLogins,
    long Errors);

public sealed record SessionSnapshot(
    Guid SessionId,
    ulong PlayerId,
    string AccountId,
    int CurrentZoneId,
    float PositionX,
    float PositionY,
    float PositionZ,
    PendingAttachmentSnapshot[] PendingAttachments);

public sealed record PendingAttachmentSnapshot(
    int ZoneId,
    string TransferToken,
    float SpawnX,
    float SpawnY,
    float SpawnZ);

public sealed record ZoneRuntimeSnapshot(
    int ZoneId,
    string Name,
    int TcpPort,
    int UdpPort,
    float MinX,
    float MaxX,
    float MinZ,
    float MaxZ,
    string RuntimeMode,
    string LifecycleState,
    DateTimeOffset LastStateChangeUtc,
    DateTimeOffset? IdleSinceUtc,
    uint Tick,
    int ActivePlayers,
    int ActiveGhosts,
    long TotalTransfersInitiated,
    float AoiRadius,
    float GhostMargin,
    float PrewarmMargin,
    float TransferInset,
    ZonePlayerSnapshot[] Players);

public sealed record ZonePlayerSnapshot(
    Guid SessionId,
    ulong PlayerId,
    float PositionX,
    float PositionY,
    float PositionZ,
    float VelocityX,
    float VelocityY,
    float VelocityZ,
    int? PendingDestinationZoneId);
