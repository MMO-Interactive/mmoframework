using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoGameplayPanel : MonoBehaviour
{
    private enum GameplayTab
    {
        Inventory,
        Progression,
        Crafting,
        Quests,
        Reputation,
        Events,
        Invasions,
        Combat
    }

    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Vector2 origin = new Vector2(16f, 144f);
    private GameplayTab _tab;
    private Vector2 _scroll;
    private bool _busy;
    private bool _collapsed;
    private string _contextNpcId = string.Empty;
    private string _contextNpcName = string.Empty;

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
        }
    }

    private async void Start()
    {
        if (client == null || !client.IsConnected)
        {
            return;
        }

        await RunAsync(LoadInitialDataAsync);
    }

    private void OnGUI()
    {
        if (client == null || !client.IsConnected)
        {
            return;
        }

        var rect = new Rect(origin.x, origin.y, 560f, _collapsed ? 92f : 500f);
        GUI.Box(rect, "Gameplay");
        DrawTabs(rect);

        GUI.Label(new Rect(rect.x + 16f, rect.y + 56f, rect.width - 32f, 20f), "Character: " + client.CurrentCharacterId);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 78f, rect.width - 32f, 20f), _busy ? "Working..." : client.StatusText);
        if (!string.IsNullOrWhiteSpace(_contextNpcName))
        {
            GUI.Label(new Rect(rect.x + 240f, rect.y + 56f, rect.width - 256f, 20f), "NPC: " + _contextNpcName);
        }

        GUI.enabled = !_busy;
        if (GUI.Button(new Rect(rect.x + rect.width - 120f, rect.y + 48f, 104f, 28f), "Refresh Tab"))
        {
            _ = RefreshCurrentTabAsync();
        }
        if (GUI.Button(new Rect(rect.x + rect.width - 232f, rect.y + 48f, 104f, 28f), _collapsed ? "Expand" : "Collapse"))
        {
            _collapsed = !_collapsed;
        }
        GUI.enabled = true;

        if (_collapsed)
        {
            return;
        }

        var viewRect = new Rect(rect.x + 16f, rect.y + 108f, rect.width - 32f, rect.height - 124f);
        GUI.Box(viewRect, string.Empty);

        switch (_tab)
        {
            case GameplayTab.Inventory:
                DrawInventory(viewRect);
                break;
            case GameplayTab.Progression:
                DrawProgression(viewRect);
                break;
            case GameplayTab.Crafting:
                DrawCrafting(viewRect);
                break;
            case GameplayTab.Quests:
                DrawQuests(viewRect);
                break;
            case GameplayTab.Reputation:
                DrawReputation(viewRect);
                break;
            case GameplayTab.Events:
                DrawWorldEvents(viewRect);
                break;
            case GameplayTab.Invasions:
                DrawInvasions(viewRect);
                break;
            case GameplayTab.Combat:
                DrawCombat(viewRect);
                break;
        }
    }

    private void DrawTabs(Rect rect)
    {
        var names = Enum.GetNames(typeof(GameplayTab));
        var width = 64f;
        for (var i = 0; i < names.Length; i++)
        {
            if (GUI.Button(new Rect(rect.x + 16f + i * (width + 6f), rect.y + 20f, width, 24f), names[i]))
            {
                _tab = (GameplayTab)i;
            }
        }
    }

    private void DrawInventory(Rect viewRect)
    {
        var inventory = client.Inventory;
        if (inventory == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No inventory loaded.");
            return;
        }

        var slots = inventory.slots ?? Array.Empty<InventorySlotData>();
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 20f + slots.Length * 28f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            GUI.Label(new Rect(12f, 12f + i * 28f, contentRect.width - 24f, 20f), "#" + slot.slotIndex + "  " + slot.itemId + " x" + slot.quantity);
        }
        GUI.EndScrollView();
    }

    private void DrawProgression(Rect viewRect)
    {
        var progression = client.Progression;
        if (progression == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No progression data loaded.");
            return;
        }

        var tracks = progression.tracks ?? Array.Empty<ProgressionTrackData>();
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 24f + tracks.Length * 38f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        for (var i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i];
            var y = 12f + i * 38f;
            GUI.Label(new Rect(12f, y, 180f, 20f), track.trackId);
            GUI.Label(new Rect(220f, y, contentRect.width - 232f, 20f), "Lvl " + track.level + "  XP " + track.experience + " / " + track.experienceToNextLevel);
        }
        GUI.EndScrollView();
    }

    private void DrawCrafting(Rect viewRect)
    {
        var recipes = GetVisibleRecipes();
        var offers = GetVisibleShopOffers();
        if (recipes.Length == 0 && offers.Length == 0)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No merchant offers or recipes loaded.");
            return;
        }

        var offerHeight = offers.Length > 0 ? 36f + (offers.Length * 64f) : 0f;
        var recipeOffset = offerHeight > 0f ? offerHeight + 12f : 0f;
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 24f + recipeOffset + recipes.Length * 64f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        if (offers.Length > 0)
        {
            GUI.Label(new Rect(12f, 12f, contentRect.width - 24f, 20f), "Merchant Stock");
            for (var i = 0; i < offers.Length; i++)
            {
                var offer = offers[i];
                var y = 36f + i * 64f;
                GUI.Box(new Rect(8f, y, contentRect.width - 16f, 54f), string.Empty);
                GUI.Label(new Rect(16f, y + 8f, 240f, 20f), offer.itemName + " x" + offer.quantity);
                GUI.Label(new Rect(16f, y + 28f, 320f, 20f), "Price: " + offer.priceItemId + " x" + offer.priceItemQuantity);
                GUI.enabled = !_busy;
                if (GUI.Button(new Rect(contentRect.width - 100f, y + 14f, 80f, 28f), "Buy"))
                {
                    _ = BuyAsync(offer.offerId);
                }
                GUI.enabled = true;
            }
        }

        if (recipes.Length > 0)
        {
            GUI.Label(new Rect(12f, 12f + recipeOffset, contentRect.width - 24f, 20f), "Crafting");
        }
        for (var i = 0; i < recipes.Length; i++)
        {
            var recipe = recipes[i];
            var y = 36f + recipeOffset + i * 64f;
            GUI.Box(new Rect(8f, y, contentRect.width - 16f, 54f), string.Empty);
            GUI.Label(new Rect(16f, y + 8f, 240f, 20f), recipe.name);
            GUI.Label(new Rect(16f, y + 28f, 320f, 20f), recipe.outputItemId + " x" + recipe.outputQuantity + " | " + FormatIngredients(recipe.ingredients));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(contentRect.width - 100f, y + 14f, 80f, 28f), "Craft"))
            {
                _ = CraftAsync(recipe.recipeId);
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
    }

    private void DrawQuests(Rect viewRect)
    {
        var board = client.QuestBoard;
        if (board == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No quest board loaded.");
            return;
        }

        var definitions = GetVisibleQuestDefinitions(board);
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 24f + definitions.Length * 92f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        for (var i = 0; i < definitions.Length; i++)
        {
            var quest = definitions[i];
            var progress = (board.progress ?? Array.Empty<QuestProgressData>()).FirstOrDefault(x => x.questId == quest.questId);
            var y = 12f + i * 92f;
            GUI.Box(new Rect(8f, y, contentRect.width - 16f, 82f), string.Empty);
            GUI.Label(new Rect(16f, y + 8f, 320f, 20f), quest.title);
            GUI.Label(new Rect(16f, y + 30f, contentRect.width - 160f, 20f), quest.description);
            GUI.Label(new Rect(16f, y + 52f, 280f, 20f), "Action " + quest.targetAction + "  " + (progress != null ? progress.progressCount : 0) + "/" + quest.targetCount);
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(contentRect.width - 196f, y + 24f, 80f, 28f), "Act"))
            {
                _ = QuestActionAsync(quest.targetAction);
            }
            if (GUI.Button(new Rect(contentRect.width - 106f, y + 24f, 80f, 28f), "Claim"))
            {
                _ = ClaimQuestAsync(quest.questId);
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
    }

    private void DrawReputation(Rect viewRect)
    {
        var reputation = client.Reputation;
        if (reputation == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No reputation profile loaded.");
            return;
        }

        var factions = reputation.factions ?? Array.Empty<FactionReputationData>();
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 24f + factions.Length * 32f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        for (var i = 0; i < factions.Length; i++)
        {
            var faction = factions[i];
            GUI.Label(new Rect(12f, 12f + i * 32f, contentRect.width - 24f, 20f), faction.factionId + "  " + faction.points + " pts  " + faction.tier);
        }
        GUI.EndScrollView();
    }

    private void DrawWorldEvents(Rect viewRect)
    {
        var board = client.WorldEvents;
        if (board == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No world events loaded.");
            return;
        }

        var events = board.events ?? Array.Empty<WorldEventData>();
        var participation = board.participation ?? Array.Empty<WorldEventParticipationData>();
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 24f + events.Length * 88f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        for (var i = 0; i < events.Length; i++)
        {
            var worldEvent = events[i];
            var playerState = participation.FirstOrDefault(x => x.eventId == worldEvent.eventId);
            var y = 12f + i * 88f;
            GUI.Box(new Rect(8f, y, contentRect.width - 16f, 78f), string.Empty);
            GUI.Label(new Rect(16f, y + 8f, 280f, 20f), worldEvent.title + " [" + worldEvent.state + "]");
            GUI.Label(new Rect(16f, y + 30f, contentRect.width - 180f, 20f), worldEvent.description);
            GUI.Label(new Rect(16f, y + 52f, 240f, 20f), "Contribution: " + (playerState != null ? playerState.contribution : 0));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(contentRect.width - 196f, y + 24f, 80f, 28f), "+1"))
            {
                _ = ContributeWorldEventAsync(worldEvent.eventId);
            }
            if (GUI.Button(new Rect(contentRect.width - 106f, y + 24f, 80f, 28f), "Claim"))
            {
                _ = ClaimWorldEventAsync(worldEvent.eventId);
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
    }

    private void DrawInvasions(Rect viewRect)
    {
        var board = client.Invasions;
        if (board == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No invasions loaded.");
            return;
        }

        var invasions = board.invasions ?? Array.Empty<InvasionData>();
        var contributions = board.contributions ?? Array.Empty<InvasionContributionData>();
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 24f + invasions.Length * 88f));
        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
        for (var i = 0; i < invasions.Length; i++)
        {
            var invasion = invasions[i];
            var contribution = contributions.FirstOrDefault(x => x.invasionId == invasion.invasionId);
            var y = 12f + i * 88f;
            GUI.Box(new Rect(8f, y, contentRect.width - 16f, 78f), string.Empty);
            GUI.Label(new Rect(16f, y + 8f, 280f, 20f), invasion.invasionId + " [" + invasion.state + "]");
            GUI.Label(new Rect(16f, y + 30f, contentRect.width - 180f, 20f), "Wave " + invasion.currentWave + "/" + invasion.totalWaves + "  Threat " + invasion.threatLevel + "  Mob " + invasion.mobTypeId);
            GUI.Label(new Rect(16f, y + 52f, 240f, 20f), "Kills: " + (contribution != null ? contribution.kills : 0));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(contentRect.width - 196f, y + 24f, 80f, 28f), "+Kill"))
            {
                _ = RecordInvasionKillAsync(invasion.invasionId);
            }
            if (GUI.Button(new Rect(contentRect.width - 106f, y + 24f, 80f, 28f), "Claim"))
            {
                _ = ClaimInvasionAsync(invasion.invasionId);
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
    }

    private void DrawCombat(Rect viewRect)
    {
        var combat = client.Combat;
        if (combat == null)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), "No combat snapshot loaded.");
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(viewRect.x + 16f, viewRect.y + 44f, 120f, 28f), "Ensure Combat"))
            {
                _ = EnsureCombatAsync();
            }
            GUI.enabled = true;
            return;
        }

        var self = (combat.combatants ?? Array.Empty<CombatantData>()).FirstOrDefault(x => x.characterId == client.CurrentCharacterId);
        GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 20f), self != null
            ? "HP " + self.hitPoints + "/" + self.maxHitPoints + "  Stamina " + self.stamina + "  Deaths " + self.deaths
            : "No combatant entry for current character.");
        GUI.enabled = !_busy;
        if (GUI.Button(new Rect(viewRect.x + 16f, viewRect.y + 44f, 120f, 28f), "Ensure Combat"))
        {
            _ = EnsureCombatAsync();
        }
        GUI.enabled = true;

        var actions = combat.recentActions ?? Array.Empty<CombatActionData>();
        var contentRect = new Rect(0f, 0f, viewRect.width - 24f, Mathf.Max(viewRect.height, 96f + actions.Length * 28f));
        _scroll = GUI.BeginScrollView(new Rect(viewRect.x, viewRect.y + 84f, viewRect.width, viewRect.height - 84f), _scroll, contentRect);
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            GUI.Label(new Rect(12f, 12f + i * 28f, contentRect.width - 24f, 20f), action.actionType + "  " + action.attackerCharacterId + " -> " + action.targetCharacterId + "  dmg " + action.damage + "  hp " + action.remainingHitPoints);
        }
        GUI.EndScrollView();
    }

    private Task RefreshCurrentTabAsync()
    {
        return _tab switch
        {
            GameplayTab.Inventory => RunAsync(client.RefreshInventoryAsync),
            GameplayTab.Progression => RunAsync(client.RefreshProgressionAsync),
            GameplayTab.Crafting => RunAsync(client.RefreshCraftingRecipesAsync),
            GameplayTab.Quests => RunAsync(client.RefreshQuestBoardAsync),
            GameplayTab.Reputation => RunAsync(client.RefreshReputationAsync),
            GameplayTab.Events => RunAsync(client.RefreshWorldEventsAsync),
            GameplayTab.Invasions => RunAsync(client.RefreshInvasionsAsync),
            GameplayTab.Combat => RunAsync(client.RefreshCombatAsync),
            _ => Task.CompletedTask
        };
    }

    private Task CraftAsync(string recipeId) => RunAsync(() => client.ExecuteRecipeAsync(recipeId));
    private Task BuyAsync(string offerId) => RunAsync(() => client.PurchaseShopOfferAsync(offerId));
    private Task EnsureCombatAsync() => RunAsync(client.EnsureCombatAsync);
    private Task QuestActionAsync(string actionId) => RunAsync(() => client.ExecuteQuestActionAsync(actionId));
    private Task ClaimQuestAsync(string questId) => RunAsync(() => client.ClaimQuestAsync(questId));
    private Task ContributeWorldEventAsync(string eventId) => RunAsync(() => client.ContributeWorldEventAsync(eventId));
    private Task ClaimWorldEventAsync(string eventId) => RunAsync(() => client.ClaimWorldEventAsync(eventId));
    private Task RecordInvasionKillAsync(string invasionId) => RunAsync(() => client.RecordInvasionKillAsync(invasionId));
    private Task ClaimInvasionAsync(string invasionId) => RunAsync(() => client.ClaimInvasionAsync(invasionId));

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await action();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task LoadInitialDataAsync()
    {
        await client.RefreshAllGameplayAsync();
    }

    private static string FormatIngredients(CraftingIngredientData[] ingredients)
    {
        if (ingredients == null || ingredients.Length == 0)
        {
            return "No ingredients";
        }

        return string.Join(", ", ingredients.Select(x => x.itemId + " x" + x.quantity).ToArray());
    }

    public void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
    }

    public void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
    }

    public void FocusInventory()
    {
        ClearNpcContext();
        _collapsed = false;
        _tab = GameplayTab.Inventory;
    }

    public void FocusCrafting()
    {
        _collapsed = false;
        _tab = GameplayTab.Crafting;
    }

    public void FocusQuests()
    {
        _collapsed = false;
        _tab = GameplayTab.Quests;
    }

    public void FocusEvents()
    {
        ClearNpcContext();
        _collapsed = false;
        _tab = GameplayTab.Events;
    }

    public void FocusCombat()
    {
        ClearNpcContext();
        _collapsed = false;
        _tab = GameplayTab.Combat;
    }

    public void FocusProgression()
    {
        ClearNpcContext();
        _collapsed = false;
        _tab = GameplayTab.Progression;
    }

    public void FocusReputation()
    {
        ClearNpcContext();
        _collapsed = false;
        _tab = GameplayTab.Reputation;
    }

    public void FocusInvasions()
    {
        ClearNpcContext();
        _collapsed = false;
        _tab = GameplayTab.Invasions;
    }

    public void OpenShopForNpc(string npcId, string npcName)
    {
        _contextNpcId = npcId ?? string.Empty;
        _contextNpcName = npcName ?? string.Empty;
        _collapsed = false;
        _tab = GameplayTab.Crafting;
        _ = RunAsync(() => client.RefreshShopCatalogAsync(_contextNpcId));
    }

    public void PrimeShopForNpc(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return;
        }

        _ = RunAsync(() => client.RefreshShopCatalogAsync(npcId));
    }

    public void OpenQuestsForNpc(string npcId, string npcName)
    {
        _contextNpcId = npcId ?? string.Empty;
        _contextNpcName = npcName ?? string.Empty;
        _collapsed = false;
        _tab = GameplayTab.Quests;
    }

    public int GetQuestCountForNpc(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return 0;
        }

        var board = client.QuestBoard;
        var definitions = board != null && board.definitions != null ? board.definitions : Array.Empty<QuestDefinitionData>();
        return definitions.Count(definition => string.Equals(definition.giverNpcId, npcId, StringComparison.OrdinalIgnoreCase));
    }

    public int GetShopOfferCountForNpc(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return 0;
        }

        var catalog = client.ShopCatalog;
        if (catalog == null || !string.Equals(catalog.npcId, npcId, StringComparison.OrdinalIgnoreCase) || catalog.offers == null)
        {
            return 0;
        }

        return catalog.offers.Length;
    }

    private void ClearNpcContext()
    {
        _contextNpcId = string.Empty;
        _contextNpcName = string.Empty;
    }

    private CraftingRecipeData[] GetVisibleRecipes()
    {
        var recipes = client.CraftingRecipes ?? Array.Empty<CraftingRecipeData>();
        if (string.IsNullOrWhiteSpace(_contextNpcId))
        {
            return recipes;
        }

        var filtered = recipes.Where(recipe => string.Equals(recipe.providerNpcId, _contextNpcId, StringComparison.OrdinalIgnoreCase)).ToArray();
        return filtered.Length > 0 ? filtered : recipes;
    }

    private ShopOfferData[] GetVisibleShopOffers()
    {
        var catalog = client.ShopCatalog;
        var offers = catalog != null && catalog.offers != null ? catalog.offers : Array.Empty<ShopOfferData>();
        if (string.IsNullOrWhiteSpace(_contextNpcId))
        {
            return offers;
        }

        if (catalog != null && string.Equals(catalog.npcId, _contextNpcId, StringComparison.OrdinalIgnoreCase))
        {
            return offers;
        }

        return Array.Empty<ShopOfferData>();
    }

    private QuestDefinitionData[] GetVisibleQuestDefinitions(QuestBoardData board)
    {
        var definitions = board.definitions ?? Array.Empty<QuestDefinitionData>();
        if (string.IsNullOrWhiteSpace(_contextNpcId))
        {
            return definitions;
        }

        var filtered = definitions.Where(definition => string.Equals(definition.giverNpcId, _contextNpcId, StringComparison.OrdinalIgnoreCase)).ToArray();
        return filtered.Length > 0 ? filtered : definitions;
    }
}
}
