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

public sealed record GameplayDefinitionsSnapshot(
    ItemDefinitionSnapshot[] Items,
    SkillDefinitionSnapshot[] Skills,
    ResourceDefinitionSnapshot[] Resources,
    ResourceNodeDefinitionSnapshot[] Nodes,
    ZoneDefinitionSnapshot[] Zones);

public sealed record ItemDefinitionSnapshot(
    string Id,
    string Name,
    int MaxStack,
    float BaseWeight);

public sealed record SkillDefinitionSnapshot(
    string Id,
    string Name,
    int MaxValue);

public sealed record ResourceDefinitionSnapshot(
    string Id,
    string Name,
    string ItemId,
    int BaseYield);

public sealed record ResourceNodeDefinitionSnapshot(
    string Id,
    int ZoneId,
    string ResourceId,
    float PositionX,
    float PositionY,
    float PositionZ,
    int RespawnSeconds);

public sealed record ZoneDefinitionSnapshot(
    int ZoneId,
    string Name,
    float MinX,
    float MaxX,
    float MinZ,
    float MaxZ);

public sealed record ModerationSnapshot(
    AccountModerationSnapshot[] Accounts,
    ModerationActionSnapshot[] RecentActions);

public sealed record AccountModerationSnapshot(
    string AccountId,
    bool IsMuted,
    bool IsBanned,
    string Reason,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

public sealed record ModerationActionSnapshot(
    long Id,
    string ActionType,
    string AccountId,
    Guid? SessionId,
    ulong? PlayerId,
    string Reason,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record InventorySnapshot(
    string AccountId,
    int Capacity,
    InventorySlotSnapshot[] Slots);

public sealed record InventorySlotSnapshot(
    int SlotIndex,
    string ItemId,
    int Quantity);

public sealed record CraftingIngredientSnapshot(
    string ItemId,
    int Quantity);

public sealed record CraftingRecipeSnapshot(
    string RecipeId,
    string Name,
    string OutputItemId,
    int OutputQuantity,
    int CraftSeconds,
    CraftingIngredientSnapshot[] Ingredients);

public sealed record CraftingResultSnapshot(
    bool Success,
    string Message,
    string AccountId,
    string RecipeId,
    InventorySnapshot Inventory);
