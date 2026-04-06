using System;

namespace MMONetworking.Client
{
[Serializable]
public sealed class InventorySnapshotData
{
    public ulong characterId;
    public int capacity;
    public InventorySlotData[] slots;
}

[Serializable]
public sealed class InventorySlotData
{
    public int slotIndex;
    public string itemId;
    public int quantity;
}

[Serializable]
public sealed class CraftingRecipeData
{
    public string recipeId;
    public string name;
    public string outputItemId;
    public int outputQuantity;
    public int craftSeconds;
    public CraftingIngredientData[] ingredients;
    public string providerNpcId;
}

[Serializable]
public sealed class CraftingIngredientData
{
    public string itemId;
    public int quantity;
}

[Serializable]
public sealed class CraftingResultData
{
    public bool success;
    public string message;
    public ulong characterId;
    public string recipeId;
    public InventorySnapshotData inventory;
}

[Serializable]
public sealed class ShopOfferData
{
    public string offerId;
    public string npcId;
    public string itemId;
    public string itemName;
    public int quantity;
    public string priceItemId;
    public int priceItemQuantity;
}

[Serializable]
public sealed class ShopCatalogData
{
    public string npcId;
    public ShopOfferData[] offers;
}

[Serializable]
public sealed class ShopPurchaseData
{
    public bool success;
    public string message;
    public string offerId;
    public ulong characterId;
    public InventorySnapshotData inventory;
}

[Serializable]
public sealed class NpcContextData
{
    public string npcId;
    public string greetingText;
    public string[] services;
    public int shopOfferCount;
    public string[] shopPreviewLines;
    public int questCount;
    public string[] questPreviewTitles;
    public bool hasTraining;
    public NpcServiceOptionData[] options;
}

[Serializable]
public sealed class NpcServiceOptionData
{
    public string actionId;
    public string label;
    public string uiHint;
}

[Serializable]
public sealed class CharacterProgressionData
{
    public ulong characterId;
    public ProgressionTrackData[] tracks;
}

[Serializable]
public sealed class ProgressionTrackData
{
    public string trackId;
    public int level;
    public int experience;
    public int experienceToNextLevel;
}

[Serializable]
public sealed class CombatSnapshotData
{
    public CombatantData[] combatants;
    public CombatActionData[] recentActions;
}

[Serializable]
public sealed class CombatantData
{
    public ulong characterId;
    public int hitPoints;
    public int maxHitPoints;
    public int stamina;
    public int deaths;
}

[Serializable]
public sealed class CombatActionData
{
    public long id;
    public string actionType;
    public ulong attackerCharacterId;
    public ulong targetCharacterId;
    public int damage;
    public int remainingHitPoints;
    public string notes;
    public string createdAtUtc;
}

[Serializable]
public sealed class ReputationProfileData
{
    public ulong characterId;
    public FactionReputationData[] factions;
    public ReputationActionData[] recentActions;
}

[Serializable]
public sealed class FactionReputationData
{
    public string factionId;
    public int points;
    public string tier;
}

[Serializable]
public sealed class ReputationActionData
{
    public long id;
    public string factionId;
    public int amount;
    public string reason;
    public string actor;
    public string createdAtUtc;
}

[Serializable]
public sealed class QuestBoardData
{
    public ulong characterId;
    public QuestDefinitionData[] definitions;
    public QuestProgressData[] progress;
    public QuestEventData[] recentEvents;
}

[Serializable]
public sealed class QuestDefinitionData
{
    public string questId;
    public string title;
    public string description;
    public string targetAction;
    public int targetCount;
    public int rewardExperience;
    public string rewardReputationFaction;
    public int rewardReputationAmount;
    public string giverNpcId;
}

[Serializable]
public sealed class QuestProgressData
{
    public ulong characterId;
    public string questId;
    public int progressCount;
    public bool isCompleted;
    public bool isClaimed;
    public string updatedAtUtc;
}

[Serializable]
public sealed class QuestEventData
{
    public long id;
    public ulong characterId;
    public string questId;
    public string eventType;
    public int amount;
    public string notes;
    public string createdAtUtc;
}

[Serializable]
public sealed class QuestClaimData
{
    public ulong characterId;
    public string questId;
    public int awardedExperience;
    public string awardedReputationFaction;
    public int awardedReputationAmount;
    public string message;
}

[Serializable]
public sealed class WorldEventBoardData
{
    public ulong characterId;
    public WorldEventData[] events;
    public WorldEventParticipationData[] participation;
    public WorldEventLogData[] recentLogs;
}

[Serializable]
public sealed class WorldEventData
{
    public string eventId;
    public string title;
    public string description;
    public string state;
    public string startsAtUtc;
    public string endsAtUtc;
    public int baseRewardExperience;
    public string rewardReputationFaction;
    public int rewardReputationAmount;
    public string createdAtUtc;
}

[Serializable]
public sealed class WorldEventParticipationData
{
    public string eventId;
    public ulong characterId;
    public int contribution;
    public bool isClaimed;
    public string updatedAtUtc;
}

[Serializable]
public sealed class WorldEventLogData
{
    public long id;
    public string eventId;
    public ulong characterId;
    public string actionType;
    public string details;
    public string createdAtUtc;
}

[Serializable]
public sealed class WorldEventClaimData
{
    public string eventId;
    public ulong characterId;
    public int contribution;
    public int awardedExperience;
    public string awardedReputationFaction;
    public int awardedReputationAmount;
    public string message;
}

[Serializable]
public sealed class InvasionBoardData
{
    public ulong characterId;
    public InvasionData[] invasions;
    public InvasionContributionData[] contributions;
    public InvasionLogData[] recentLogs;
}

[Serializable]
public sealed class InvasionData
{
    public string invasionId;
    public int zoneId;
    public string mobTypeId;
    public int totalWaves;
    public int currentWave;
    public string state;
    public int threatLevel;
    public string startsAtUtc;
    public string endsAtUtc;
}

[Serializable]
public sealed class InvasionContributionData
{
    public string invasionId;
    public ulong characterId;
    public int kills;
    public bool isClaimed;
    public string updatedAtUtc;
}

[Serializable]
public sealed class InvasionLogData
{
    public long id;
    public string invasionId;
    public ulong characterId;
    public string actionType;
    public string details;
    public string createdAtUtc;
}

[Serializable]
public sealed class InvasionClaimData
{
    public string invasionId;
    public ulong characterId;
    public int kills;
    public int awardedExperience;
    public string awardedReputationFaction;
    public int awardedReputationAmount;
    public string message;
}

[Serializable]
public sealed class GameplayBundleData
{
    public InventorySnapshotData inventory;
    public CraftingRecipeData[] craftingRecipes;
    public CharacterProgressionData progression;
    public CombatSnapshotData combat;
    public ReputationProfileData reputation;
    public QuestBoardData questBoard;
    public WorldEventBoardData worldEvents;
    public InvasionBoardData invasions;
}

[Serializable]
public sealed class ErrorEnvelopeData
{
    public string error;
}

[Serializable]
public sealed class ArrayWrapper<T>
{
    public T[] items;
}
}
