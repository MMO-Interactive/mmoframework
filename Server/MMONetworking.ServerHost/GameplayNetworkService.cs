using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

namespace MMONetworking.ServerHost;

public sealed class GameplayNetworkService
{
    private readonly InventoryStore _inventoryStore;
    private readonly CraftingStore _craftingStore;
    private readonly ShopStore _shopStore;
    private readonly CharacterProgressionStore _progressionStore;
    private readonly CombatStore _combatStore;
    private readonly ReputationStore _reputationStore;
    private readonly QuestStore _questStore;
    private readonly WorldEventStore _worldEventStore;
    private readonly InvasionStore _invasionStore;
    private readonly GameplayDefinitionStore _gameplayDefinitionStore;

    public GameplayNetworkService(
        InventoryStore inventoryStore,
        CraftingStore craftingStore,
        ShopStore shopStore,
        CharacterProgressionStore progressionStore,
        CombatStore combatStore,
        ReputationStore reputationStore,
        QuestStore questStore,
        WorldEventStore worldEventStore,
        InvasionStore invasionStore,
        GameplayDefinitionStore gameplayDefinitionStore)
    {
        _inventoryStore = inventoryStore;
        _craftingStore = craftingStore;
        _shopStore = shopStore;
        _progressionStore = progressionStore;
        _combatStore = combatStore;
        _reputationStore = reputationStore;
        _questStore = questStore;
        _worldEventStore = worldEventStore;
        _invasionStore = invasionStore;
        _gameplayDefinitionStore = gameplayDefinitionStore;
    }

    public GameplayServiceResponseMessage Handle(Guid sessionId, ulong characterId, uint requestId, GameplayServiceKind serviceKind, string payloadJson)
    {
        try
        {
            var payload = serviceKind switch
            {
                GameplayServiceKind.Inventory => Serialize(_inventoryStore.GetInventory(characterId)),
                GameplayServiceKind.CraftingRecipes => Serialize(new ArrayEnvelope<CraftingRecipeSnapshot>(_craftingStore.GetRecipes())),
                GameplayServiceKind.CraftRecipe => HandleCraftRecipe(characterId, payloadJson),
                GameplayServiceKind.ShopCatalog => HandleShopCatalog(payloadJson),
                GameplayServiceKind.ShopPurchase => HandleShopPurchase(characterId, payloadJson),
                GameplayServiceKind.Progression => Serialize(_progressionStore.Get(characterId)),
                GameplayServiceKind.CombatSnapshot => Serialize(_combatStore.GetSnapshot()),
                GameplayServiceKind.CombatEnsure => Serialize(_combatStore.EnsureCombatant(characterId)),
                GameplayServiceKind.CombatAttack => HandleCombatAttack(characterId, payloadJson),
                GameplayServiceKind.Reputation => Serialize(_reputationStore.GetProfile(characterId)),
                GameplayServiceKind.QuestBoard => Serialize(_questStore.GetBoard(characterId)),
                GameplayServiceKind.QuestAction => HandleQuestAction(characterId, payloadJson),
                GameplayServiceKind.QuestClaim => HandleQuestClaim(characterId, payloadJson),
                GameplayServiceKind.WorldEvents => Serialize(_worldEventStore.GetBoard(characterId)),
                GameplayServiceKind.WorldEventContribute => HandleWorldEventContribute(characterId, payloadJson),
                GameplayServiceKind.WorldEventClaim => HandleWorldEventClaim(characterId, payloadJson),
                GameplayServiceKind.Invasions => Serialize(_invasionStore.GetBoard(characterId)),
                GameplayServiceKind.InvasionRecordKill => HandleInvasionRecordKill(characterId, payloadJson),
                GameplayServiceKind.InvasionClaim => HandleInvasionClaim(characterId, payloadJson),
                GameplayServiceKind.FullState => Serialize(BuildFullState(characterId)),
                GameplayServiceKind.NpcContext => HandleNpcContext(characterId, payloadJson),
                _ => throw new InvalidOperationException("Unsupported gameplay service request.")
            };

            return new GameplayServiceResponseMessage(sessionId, requestId, serviceKind, true, payload, string.Empty);
        }
        catch (Exception ex)
        {
            return new GameplayServiceResponseMessage(sessionId, requestId, serviceKind, false, string.Empty, ex.Message);
        }
    }

    public void PersistGameplayReward(ulong characterId, string itemId, int itemQuantity, int maxStack, string skillTrackId, int skillExperience)
    {
        if (!string.IsNullOrWhiteSpace(itemId) && itemQuantity > 0)
        {
            _inventoryStore.AddItem(characterId, itemId, itemQuantity, maxStack > 0 ? maxStack : 200);
        }

        if (!string.IsNullOrWhiteSpace(skillTrackId) && skillExperience > 0)
        {
            _progressionStore.GrantExperience(characterId, skillTrackId, skillExperience);
        }
    }

    private string HandleCraftRecipe(ulong characterId, string payloadJson)
    {
        var request = Deserialize<CraftRecipeRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.RecipeId))
        {
            throw new InvalidOperationException("recipeId is required.");
        }

        var recipe = _craftingStore.GetRecipes().FirstOrDefault(entry => string.Equals(entry.RecipeId, request.RecipeId, StringComparison.OrdinalIgnoreCase));
        if (recipe is null)
        {
            throw new InvalidOperationException("Recipe not found.");
        }

        try
        {
            var inventory = _inventoryStore.Craft(characterId, recipe, request.OutputMaxStack <= 0 ? 200 : request.OutputMaxStack, request.Capacity <= 0 ? 40 : request.Capacity);
            _progressionStore.GrantExperience(characterId, "crafting", Math.Max(5, recipe.OutputQuantity * 5));
            return Serialize(new CraftingResultSnapshot(true, $"Crafted {recipe.OutputQuantity} {recipe.OutputItemId}.", characterId, recipe.RecipeId, inventory));
        }
        catch (Exception ex)
        {
            var inventory = _inventoryStore.GetInventory(characterId, request.Capacity <= 0 ? 40 : request.Capacity);
            return Serialize(new CraftingResultSnapshot(false, ex.Message, characterId, request.RecipeId, inventory));
        }
    }

    private string HandleShopCatalog(string payloadJson)
    {
        var request = Deserialize<ShopCatalogRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.NpcId))
        {
            throw new InvalidOperationException("npcId is required.");
        }

        return Serialize(_shopStore.GetCatalog(request.NpcId.Trim()));
    }

    private string HandleShopPurchase(ulong characterId, string payloadJson)
    {
        var request = Deserialize<ShopPurchaseRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.OfferId))
        {
            throw new InvalidOperationException("offerId is required.");
        }

        var offer = _shopStore.GetOffer(request.OfferId.Trim());
        var inventory = _inventoryStore.Exchange(
            characterId,
            offer.PriceItemId,
            offer.PriceItemQuantity,
            offer.ItemId,
            offer.Quantity,
            request.OutputMaxStack <= 0 ? 200 : request.OutputMaxStack,
            request.Capacity <= 0 ? 40 : request.Capacity);

        return Serialize(new ShopPurchaseSnapshot(
            true,
            "Purchased " + offer.ItemName + ".",
            offer.OfferId,
            characterId,
            inventory));
    }

    private string HandleNpcContext(ulong characterId, string payloadJson)
    {
        var request = Deserialize<NpcContextRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.NpcId))
        {
            throw new InvalidOperationException("npcId is required.");
        }

        var npcId = request.NpcId.Trim();
        var npc = _gameplayDefinitionStore
            .GetSnapshot()
            .Npcs
            .FirstOrDefault(entry => string.Equals(entry.NpcId, npcId, StringComparison.OrdinalIgnoreCase));

        if (npc is null)
        {
            npc = BuildFallbackNpcDefinition(npcId);
        }

        if (npc is null)
        {
            throw new InvalidOperationException("NPC not found.");
        }

        var offers = _shopStore.GetCatalog(npcId).Offers ?? Array.Empty<ShopOfferSnapshot>();
        var quests = _questStore.GetBoard(characterId).Definitions ?? Array.Empty<QuestDefinitionSnapshot>();
        var npcQuests = quests
            .Where(definition => string.Equals(definition.GiverNpcId, npcId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var questCount = npcQuests.Length;
        var services = npc.Services ?? Array.Empty<string>();
        var hasTraining = ContainsService(services, "training") || ContainsService(services, "progression") || (!string.IsNullOrWhiteSpace(npc.PrimaryRole) && npc.PrimaryRole.IndexOf("trainer", StringComparison.OrdinalIgnoreCase) >= 0);
        var options = BuildNpcOptions(npc, offers.Length, questCount, hasTraining);
        var shopPreviewLines = offers
            .Take(3)
            .Select(offer => offer.ItemName + " x" + offer.Quantity + " for " + offer.PriceItemId + " x" + offer.PriceItemQuantity)
            .ToArray();
        var questPreviewTitles = npcQuests
            .Take(3)
            .Select(definition => definition.Title)
            .ToArray();

        return Serialize(new NpcContextSnapshot(
            npc.NpcId,
            npc.GreetingText ?? string.Empty,
            services,
            offers.Length,
            shopPreviewLines,
            questCount,
            questPreviewTitles,
            hasTraining,
            options));
    }

    private string HandleCombatAttack(ulong characterId, string payloadJson)
    {
        var request = Deserialize<CombatAttackRequest>(payloadJson);
        if (request.TargetCharacterId == 0)
        {
            throw new InvalidOperationException("targetCharacterId is required.");
        }

        return Serialize(_combatStore.Attack(characterId, request.TargetCharacterId, request.BaseDamage <= 0 ? 10 : request.BaseDamage));
    }

    private string HandleQuestAction(ulong characterId, string payloadJson)
    {
        var request = Deserialize<QuestActionRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.ActionId))
        {
            throw new InvalidOperationException("actionId is required.");
        }

        return Serialize(_questStore.RecordAction(characterId, request.ActionId.Trim(), request.Amount));
    }

    private string HandleQuestClaim(ulong characterId, string payloadJson)
    {
        var request = Deserialize<QuestClaimRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.QuestId))
        {
            throw new InvalidOperationException("questId is required.");
        }

        var claim = _questStore.Claim(characterId, request.QuestId.Trim());
        if (claim.AwardedExperience > 0)
        {
            _progressionStore.GrantExperience(claim.CharacterId, "adventuring", claim.AwardedExperience);
        }

        if (!string.IsNullOrWhiteSpace(claim.AwardedReputationFaction) && claim.AwardedReputationAmount != 0)
        {
            _reputationStore.Award(claim.CharacterId, claim.AwardedReputationFaction, claim.AwardedReputationAmount, $"Quest reward: {claim.QuestId}", "quest-system");
        }

        return Serialize(claim);
    }

    private string HandleWorldEventContribute(ulong characterId, string payloadJson)
    {
        var request = Deserialize<WorldEventContributionRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            throw new InvalidOperationException("eventId is required.");
        }

        return Serialize(_worldEventStore.AddContribution(request.EventId.Trim(), characterId, request.Amount));
    }

    private string HandleWorldEventClaim(ulong characterId, string payloadJson)
    {
        var request = Deserialize<WorldEventClaimRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            throw new InvalidOperationException("eventId is required.");
        }

        var claim = _worldEventStore.Claim(request.EventId.Trim(), characterId);
        if (claim.AwardedExperience > 0)
        {
            _progressionStore.GrantExperience(claim.CharacterId, "adventuring", claim.AwardedExperience);
        }

        if (!string.IsNullOrWhiteSpace(claim.AwardedReputationFaction) && claim.AwardedReputationAmount != 0)
        {
            _reputationStore.Award(claim.CharacterId, claim.AwardedReputationFaction, claim.AwardedReputationAmount, $"World event reward: {claim.EventId}", "world-event-system");
        }

        return Serialize(claim);
    }

    private string HandleInvasionRecordKill(ulong characterId, string payloadJson)
    {
        var request = Deserialize<InvasionRecordKillRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.InvasionId))
        {
            throw new InvalidOperationException("invasionId is required.");
        }

        return Serialize(_invasionStore.RecordKill(request.InvasionId.Trim(), characterId, request.Kills));
    }

    private string HandleInvasionClaim(ulong characterId, string payloadJson)
    {
        var request = Deserialize<InvasionClaimRequest>(payloadJson);
        if (string.IsNullOrWhiteSpace(request.InvasionId))
        {
            throw new InvalidOperationException("invasionId is required.");
        }

        var claim = _invasionStore.Claim(request.InvasionId.Trim(), characterId);
        _progressionStore.GrantExperience(claim.CharacterId, "combat", claim.AwardedExperience);
        _reputationStore.Award(claim.CharacterId, claim.AwardedReputationFaction, claim.AwardedReputationAmount, $"Invasion reward: {claim.InvasionId}", "invasion-system");
        return Serialize(claim);
    }

    private GameplayBundleSnapshot BuildFullState(ulong characterId)
        => new(
            _inventoryStore.GetInventory(characterId),
            _craftingStore.GetRecipes(),
            _progressionStore.Get(characterId),
            _combatStore.GetSnapshot(),
            _reputationStore.GetProfile(characterId),
            _questStore.GetBoard(characterId),
            _worldEventStore.GetBoard(characterId),
            _invasionStore.GetBoard(characterId));

    private static T Deserialize<T>(string payloadJson) where T : class, new()
        => string.IsNullOrWhiteSpace(payloadJson)
            ? new T()
            : (JsonSerializer.Deserialize<T>(payloadJson, JsonOptions) ?? new T());

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class ArrayEnvelope<T>
    {
        public ArrayEnvelope(T[] items)
        {
            Items = items;
        }

        public T[] Items { get; }
    }

    private sealed class CraftRecipeRequest
    {
        public string RecipeId { get; set; } = string.Empty;
        public int OutputMaxStack { get; set; }
        public int Capacity { get; set; }
    }

    private sealed class CombatAttackRequest
    {
        public ulong TargetCharacterId { get; set; }
        public int BaseDamage { get; set; }
    }

    private sealed class ShopCatalogRequest
    {
        public string NpcId { get; set; } = string.Empty;
    }

    private sealed class ShopPurchaseRequest
    {
        public string OfferId { get; set; } = string.Empty;
        public int OutputMaxStack { get; set; }
        public int Capacity { get; set; }
    }

    private sealed class NpcContextRequest
    {
        public string NpcId { get; set; } = string.Empty;
    }

    private sealed record NpcContextSnapshot(
        string NpcId,
        string GreetingText,
        string[] Services,
        int ShopOfferCount,
        string[] ShopPreviewLines,
        int QuestCount,
        string[] QuestPreviewTitles,
        bool HasTraining,
        NpcServiceOptionSnapshot[] Options);

    private sealed record NpcServiceOptionSnapshot(
        string ActionId,
        string Label,
        string UiHint);

    private static bool ContainsService(IReadOnlyList<string> services, string value)
        => services.Any(service => string.Equals(service, value, StringComparison.OrdinalIgnoreCase));

    private static NpcServiceOptionSnapshot[] BuildNpcOptions(NpcDefinitionSnapshot npc, int shopOfferCount, int questCount, bool hasTraining)
    {
        var authoredOptions = npc.ServiceOptions ?? Array.Empty<NpcServiceDefinitionSnapshot>();
        if (authoredOptions.Length > 0)
        {
            return authoredOptions
                .GroupBy(option => option.ActionId, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var option = group.First();
                    var uiHint = string.IsNullOrWhiteSpace(option.UiHint)
                        ? BuildDynamicHint(option.ActionId, shopOfferCount, questCount, hasTraining)
                        : option.UiHint;
                    return new NpcServiceOptionSnapshot(option.ActionId, option.Label, uiHint);
                })
                .ToArray();
        }

        var options = new System.Collections.Generic.List<NpcServiceOptionSnapshot>();

        if (ContainsService(npc.Services ?? Array.Empty<string>(), "shop") ||
            ContainsService(npc.Services ?? Array.Empty<string>(), "crafting") ||
            (!string.IsNullOrWhiteSpace(npc.PrimaryRole) && npc.PrimaryRole.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            options.Add(new NpcServiceOptionSnapshot(
                "shop",
                "Shop",
                shopOfferCount > 0 ? $"{shopOfferCount} offers ready" : "No offers loaded"));
        }

        if (ContainsService(npc.Services ?? Array.Empty<string>(), "quests") ||
            (!string.IsNullOrWhiteSpace(npc.PrimaryRole) && npc.PrimaryRole.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            options.Add(new NpcServiceOptionSnapshot(
                "quests",
                "Quests",
                questCount > 0 ? $"{questCount} quests available" : "No authored quests"));
        }

        if (hasTraining)
        {
            options.Add(new NpcServiceOptionSnapshot(
                "training",
                "Training",
                "Review progression and skill tracks"));
        }

        if (options.Count == 0)
        {
            options.Add(new NpcServiceOptionSnapshot(
                "talk",
                "Talk",
                "General conversation"));
        }

        return options.ToArray();
    }

    private static string BuildDynamicHint(string actionId, int shopOfferCount, int questCount, bool hasTraining)
        => (actionId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "shop" => shopOfferCount > 0 ? $"{shopOfferCount} offers ready" : "No offers loaded",
            "quests" => questCount > 0 ? $"{questCount} quests available" : "No authored quests",
            "training" => hasTraining ? "Review progression and skill tracks" : string.Empty,
            _ => string.Empty
        };

    private static NpcDefinitionSnapshot? BuildFallbackNpcDefinition(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return null;
        }

        var trimmed = npcId.Trim();
        if (trimmed.StartsWith("merchant-", StringComparison.OrdinalIgnoreCase))
        {
            return new NpcDefinitionSnapshot(
                trimmed,
                0,
                "merchant",
                "Quartermaster",
                0f,
                0f,
                0f,
                "shop",
                new[] { "shop", "crafting" },
                "Supplies for the road, tools for the trade, and a fair barter if your pack is worth opening.",
                new[]
                {
                    new NpcServiceDefinitionSnapshot("shop", "Shop", "Browse merchant stock")
                });
        }

        if (trimmed.StartsWith("questgiver-", StringComparison.OrdinalIgnoreCase))
        {
            return new NpcDefinitionSnapshot(
                trimmed,
                0,
                "quest_giver",
                "Warden",
                0f,
                0f,
                0f,
                "quest",
                new[] { "quests" },
                "Every frontier needs hands willing to work. If you want purpose, I have tasks that matter.",
                new[]
                {
                    new NpcServiceDefinitionSnapshot("quests", "Quests", "Review available work")
                });
        }

        if (trimmed.StartsWith("trainer-", StringComparison.OrdinalIgnoreCase))
        {
            return new NpcDefinitionSnapshot(
                trimmed,
                0,
                "trainer",
                "Trainer",
                0f,
                0f,
                0f,
                "trainer",
                new[] { "training", "progression" },
                "Skill is earned, not granted. Show me what you've practiced, and I'll show you where to sharpen it next.",
                new[]
                {
                    new NpcServiceDefinitionSnapshot("training", "Training", "Review skill progression")
                });
        }

        return null;
    }

    private sealed class QuestActionRequest
    {
        public string ActionId { get; set; } = string.Empty;
        public int Amount { get; set; }
    }

    private sealed class QuestClaimRequest
    {
        public string QuestId { get; set; } = string.Empty;
    }

    private sealed class WorldEventContributionRequest
    {
        public string EventId { get; set; } = string.Empty;
        public int Amount { get; set; }
    }

    private sealed class WorldEventClaimRequest
    {
        public string EventId { get; set; } = string.Empty;
    }

    private sealed class InvasionRecordKillRequest
    {
        public string InvasionId { get; set; } = string.Empty;
        public int Kills { get; set; }
    }

    private sealed class InvasionClaimRequest
    {
        public string InvasionId { get; set; } = string.Empty;
    }
}
