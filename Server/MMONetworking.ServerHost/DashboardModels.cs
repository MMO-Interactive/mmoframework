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
    string AccountName,
    int CurrentZoneId,
    float PositionX,
    float PositionY,
    float PositionZ,
    DateTimeOffset LastTcpSeenUtc,
    DateTimeOffset LastUdpSeenUtc,
    DateTimeOffset? DisconnectGraceDeadlineUtc,
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
    ZonePlayerSnapshot[] Players,
    ZoneMobSnapshot[] Mobs);

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

public sealed record ZoneMobSnapshot(
    string MobId,
    string MobTypeId,
    float PositionX,
    float PositionY,
    float PositionZ,
    float VelocityX,
    float VelocityY,
    float VelocityZ,
    string State);

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

public sealed record CharacterSummarySnapshot(
    ulong CharacterId,
    string CharacterName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSelectedAtUtc);

public sealed record AccountCharacterListSnapshot(
    string AccountId,
    string AccountName,
    ulong SelectedCharacterId,
    CharacterSummarySnapshot[] Characters);

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
    ulong CharacterId,
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
    ulong CharacterId,
    string RecipeId,
    InventorySnapshot Inventory);

public sealed record CharacterProgressionSnapshot(
    ulong CharacterId,
    ProgressionTrackSnapshot[] Tracks);

public sealed record ProgressionTrackSnapshot(
    string TrackId,
    int Level,
    int Experience,
    int ExperienceToNextLevel);

public sealed record CombatantSnapshot(
    ulong CharacterId,
    int HitPoints,
    int MaxHitPoints,
    int Stamina,
    int Deaths);

public sealed record CombatActionSnapshot(
    long Id,
    string ActionType,
    ulong AttackerCharacterId,
    ulong TargetCharacterId,
    int Damage,
    int RemainingHitPoints,
    string Notes,
    DateTimeOffset CreatedAtUtc);

public sealed record CombatSnapshot(
    CombatantSnapshot[] Combatants,
    CombatActionSnapshot[] RecentActions);

public sealed record FactionReputationSnapshot(
    string FactionId,
    int Points,
    string Tier);

public sealed record ReputationActionSnapshot(
    long Id,
    string FactionId,
    int Amount,
    string Reason,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record ReputationProfileSnapshot(
    ulong CharacterId,
    FactionReputationSnapshot[] Factions,
    ReputationActionSnapshot[] RecentActions);

public sealed record QuestDefinitionSnapshot(
    string QuestId,
    string Title,
    string Description,
    string TargetAction,
    int TargetCount,
    int RewardExperience,
    string RewardReputationFaction,
    int RewardReputationAmount);

public sealed record QuestProgressSnapshot(
    ulong CharacterId,
    string QuestId,
    int ProgressCount,
    bool IsCompleted,
    bool IsClaimed,
    DateTimeOffset UpdatedAtUtc);

public sealed record QuestBoardSnapshot(
    ulong CharacterId,
    QuestDefinitionSnapshot[] Definitions,
    QuestProgressSnapshot[] Progress,
    QuestEventSnapshot[] RecentEvents);

public sealed record QuestEventSnapshot(
    long Id,
    ulong CharacterId,
    string QuestId,
    string EventType,
    int Amount,
    string Notes,
    DateTimeOffset CreatedAtUtc);

public sealed record QuestClaimSnapshot(
    ulong CharacterId,
    string QuestId,
    int AwardedExperience,
    string AwardedReputationFaction,
    int AwardedReputationAmount,
    string Message);

public sealed record WorldEventSnapshot(
    string EventId,
    string Title,
    string Description,
    string State,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int BaseRewardExperience,
    string RewardReputationFaction,
    int RewardReputationAmount,
    DateTimeOffset CreatedAtUtc);

public sealed record WorldEventParticipationSnapshot(
    string EventId,
    ulong CharacterId,
    int Contribution,
    bool IsClaimed,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorldEventBoardSnapshot(
    ulong CharacterId,
    WorldEventSnapshot[] Events,
    WorldEventParticipationSnapshot[] Participation,
    WorldEventLogSnapshot[] RecentLogs);

public sealed record WorldEventClaimSnapshot(
    string EventId,
    ulong CharacterId,
    int Contribution,
    int AwardedExperience,
    string AwardedReputationFaction,
    int AwardedReputationAmount,
    string Message);

public sealed record WorldEventLeaderboardEntrySnapshot(
    int Rank,
    ulong CharacterId,
    int Contribution,
    bool IsClaimed);

public sealed record WorldEventLogSnapshot(
    long Id,
    string EventId,
    ulong CharacterId,
    string ActionType,
    string Details,
    DateTimeOffset CreatedAtUtc);

public sealed record InvasionSnapshot(
    string InvasionId,
    int ZoneId,
    string MobTypeId,
    int TotalWaves,
    int CurrentWave,
    string State,
    int ThreatLevel,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

public sealed record InvasionContributionSnapshot(
    string InvasionId,
    ulong CharacterId,
    int Kills,
    bool IsClaimed,
    DateTimeOffset UpdatedAtUtc);

public sealed record InvasionLogSnapshot(
    long Id,
    string InvasionId,
    ulong CharacterId,
    string ActionType,
    string Details,
    DateTimeOffset CreatedAtUtc);

public sealed record InvasionBoardSnapshot(
    ulong CharacterId,
    InvasionSnapshot[] Invasions,
    InvasionContributionSnapshot[] Contributions,
    InvasionLogSnapshot[] RecentLogs);

public sealed record InvasionClaimSnapshot(
    string InvasionId,
    ulong CharacterId,
    int Kills,
    int AwardedExperience,
    string AwardedReputationFaction,
    int AwardedReputationAmount,
    string Message);
