using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace MMONetworking.ServerHost;

public sealed class ManagementDashboardHost
{
    private readonly GatewayHost _gatewayHost;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ZoneSupervisor _zoneSupervisor;
    private readonly GameplayDefinitionStore _gameplayDefinitions;
    private readonly ModerationStore _moderationStore;
    private readonly InventoryStore _inventoryStore;
    private readonly CraftingStore _craftingStore;
    private readonly CharacterProgressionStore _progressionStore;
    private readonly CombatStore _combatStore;
    private readonly ReputationStore _reputationStore;
    private readonly QuestStore _questStore;
    private readonly WorldEventStore _worldEventStore;
    private readonly InvasionStore _invasionStore;
    private readonly int _port;

    public ManagementDashboardHost(GatewayHost gatewayHost, SessionRegistry sessionRegistry, ZoneSupervisor zoneSupervisor, GameplayDefinitionStore gameplayDefinitions, ModerationStore moderationStore, InventoryStore inventoryStore, CraftingStore craftingStore, CharacterProgressionStore progressionStore, CombatStore combatStore, ReputationStore reputationStore, QuestStore questStore, WorldEventStore worldEventStore, InvasionStore invasionStore, int port)
    {
        _gatewayHost = gatewayHost;
        _sessionRegistry = sessionRegistry;
        _zoneSupervisor = zoneSupervisor;
        _gameplayDefinitions = gameplayDefinitions;
        _moderationStore = moderationStore;
        _inventoryStore = inventoryStore;
        _craftingStore = craftingStore;
        _progressionStore = progressionStore;
        _combatStore = combatStore;
        _reputationStore = reputationStore;
        _questStore = questStore;
        _worldEventStore = worldEventStore;
        _invasionStore = invasionStore;
        _port = port;
    }

    public DashboardSnapshot CreateSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            _gatewayHost.CreateDashboardSnapshot(),
            _sessionRegistry.CreateDashboardSnapshot(),
            _zoneSupervisor.CreateDashboardSnapshot());

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        var app = builder.Build();

        app.MapGet("/api/dashboard", () => Results.Json(CreateSnapshot()));
        app.MapGet("/api/gameplay-definitions", () => Results.Json(_gameplayDefinitions.GetSnapshot()));
        app.MapPut("/api/gameplay-definitions", async (HttpContext context) =>
        {
            try
            {
                var incoming = await JsonSerializer.DeserializeAsync<GameplayDefinitionsSnapshot>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    context.RequestAborted).ConfigureAwait(false);

                if (incoming is null)
                {
                    return Results.BadRequest(new { error = "Request body was empty or invalid JSON." });
                }

                var saved = _gameplayDefinitions.Update(incoming);
                return Results.Json(saved);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/moderation", () => Results.Json(_moderationStore.CreateSnapshot()));
        app.MapPost("/api/moderation/account", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<ModerationAccountRequest>(
                context.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                context.RequestAborted).ConfigureAwait(false);

            if (request is null || string.IsNullOrWhiteSpace(request.AccountId))
            {
                return Results.BadRequest(new { error = "accountId is required." });
            }

            _moderationStore.SetAccountState(
                request.AccountId.Trim(),
                request.IsMuted,
                request.IsBanned,
                request.Reason ?? string.Empty,
                string.IsNullOrWhiteSpace(request.Actor) ? "dashboard" : request.Actor.Trim());

            return Results.Json(_moderationStore.CreateSnapshot());
        });
        app.MapPost("/api/moderation/kick", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<ModerationKickRequest>(
                context.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                context.RequestAborted).ConfigureAwait(false);

            if (request is null || request.SessionId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "sessionId is required." });
            }

            if (_sessionRegistry.TryRemove(request.SessionId, out var removed) && removed is not null)
            {
                _moderationStore.RecordKick(
                    removed.SessionId,
                    removed.PlayerId,
                    removed.AccountId,
                    request.Reason ?? "Kicked from dashboard",
                    string.IsNullOrWhiteSpace(request.Actor) ? "dashboard" : request.Actor.Trim());
            }

            return Results.Json(_moderationStore.CreateSnapshot());
        });
        app.MapGet("/api/inventory/{accountId}", (string accountId) =>
        {
            try
            {
                return Results.Json(_inventoryStore.GetInventory(accountId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/inventory/add", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InventoryAddRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_inventoryStore.AddItem(request.AccountId, request.ItemId, request.Quantity, request.MaxStack, request.Capacity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/inventory/move", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InventoryMoveRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_inventoryStore.Move(request.AccountId, request.FromSlot, request.ToSlot, request.Capacity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/inventory/split", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InventorySplitRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_inventoryStore.Split(request.AccountId, request.FromSlot, request.ToSlot, request.Quantity, request.Capacity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/inventory/remove", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InventoryRemoveRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_inventoryStore.Remove(request.AccountId, request.SlotIndex, request.Quantity, request.Capacity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/crafting/recipes", () => Results.Json(_craftingStore.GetRecipes()));
        app.MapPost("/api/crafting/recipes/upsert", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<CraftingRecipeSnapshot>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                var recipe = _craftingStore.UpsertRecipe(request);
                return Results.Json(recipe);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/crafting/execute", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<CraftingExecuteRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.RecipeId))
            {
                return Results.BadRequest(new { error = "accountId and recipeId are required." });
            }

            try
            {
                var recipe = _craftingStore.GetRecipes().FirstOrDefault(entry => string.Equals(entry.RecipeId, request.RecipeId, StringComparison.OrdinalIgnoreCase));
                if (recipe is null)
                {
                    return Results.BadRequest(new { error = "Recipe not found." });
                }

                var inventory = _inventoryStore.Craft(request.AccountId, recipe, request.OutputMaxStack <= 0 ? 200 : request.OutputMaxStack, request.Capacity <= 0 ? 40 : request.Capacity);
                _progressionStore.GrantExperience(request.AccountId, "crafting", Math.Max(5, recipe.OutputQuantity * 5));
                return Results.Json(new CraftingResultSnapshot(true, $"Crafted {recipe.OutputQuantity} {recipe.OutputItemId}.", request.AccountId, recipe.RecipeId, inventory));
            }
            catch (Exception ex)
            {
                var snapshot = _inventoryStore.GetInventory(request.AccountId, request.Capacity <= 0 ? 40 : request.Capacity);
                return Results.BadRequest(new CraftingResultSnapshot(false, ex.Message, request.AccountId, request.RecipeId, snapshot));
            }
        });
        app.MapGet("/api/progression/{accountId}", (string accountId) =>
        {
            try
            {
                return Results.Json(_progressionStore.Get(accountId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/progression/grant", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<GrantProgressionRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_progressionStore.GrantExperience(request.AccountId, request.TrackId, request.Experience));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/combat", () => Results.Json(_combatStore.GetSnapshot()));
        app.MapPost("/api/combat/ensure", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<CombatEnsureRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AccountId))
            {
                return Results.BadRequest(new { error = "accountId is required." });
            }

            try
            {
                return Results.Json(_combatStore.EnsureCombatant(request.AccountId.Trim()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/combat/attack", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<CombatAttackRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AttackerAccountId) || string.IsNullOrWhiteSpace(request.TargetAccountId))
            {
                return Results.BadRequest(new { error = "attackerAccountId and targetAccountId are required." });
            }

            try
            {
                return Results.Json(_combatStore.Attack(request.AttackerAccountId.Trim(), request.TargetAccountId.Trim(), request.BaseDamage <= 0 ? 10 : request.BaseDamage));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/reputation/{accountId}", (string accountId) =>
        {
            try
            {
                return Results.Json(_reputationStore.GetProfile(accountId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/reputation/award", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<ReputationAwardRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.FactionId))
            {
                return Results.BadRequest(new { error = "accountId and factionId are required." });
            }

            try
            {
                return Results.Json(_reputationStore.Award(
                    request.AccountId.Trim(),
                    request.FactionId.Trim(),
                    request.Amount,
                    string.IsNullOrWhiteSpace(request.Reason) ? "manual adjustment" : request.Reason.Trim(),
                    string.IsNullOrWhiteSpace(request.Actor) ? "dashboard" : request.Actor.Trim()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/quests/{accountId}", (string accountId) =>
        {
            try
            {
                return Results.Json(_questStore.GetBoard(accountId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/quests/definitions/upsert", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<QuestDefinitionUpsertRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_questStore.UpsertDefinition(request.Definition, string.IsNullOrWhiteSpace(request.Actor) ? "dashboard" : request.Actor.Trim()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/quests/progress", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<QuestProgressRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.QuestId))
            {
                return Results.BadRequest(new { error = "accountId and questId are required." });
            }

            try
            {
                return Results.Json(_questStore.RecordProgress(request.AccountId.Trim(), request.QuestId.Trim(), request.Amount));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/quests/action", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<QuestActionRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.ActionId))
            {
                return Results.BadRequest(new { error = "accountId and actionId are required." });
            }

            try
            {
                return Results.Json(_questStore.RecordAction(request.AccountId.Trim(), request.ActionId.Trim(), request.Amount));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/quests/claim", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<QuestClaimRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.QuestId))
            {
                return Results.BadRequest(new { error = "accountId and questId are required." });
            }

            try
            {
                var claim = _questStore.Claim(request.AccountId.Trim(), request.QuestId.Trim());
                if (claim.AwardedExperience > 0)
                {
                    _progressionStore.GrantExperience(claim.AccountId, "adventuring", claim.AwardedExperience);
                }

                if (!string.IsNullOrWhiteSpace(claim.AwardedReputationFaction) && claim.AwardedReputationAmount != 0)
                {
                    _reputationStore.Award(claim.AccountId, claim.AwardedReputationFaction, claim.AwardedReputationAmount, $"Quest reward: {claim.QuestId}", "quest-system");
                }

                return Results.Json(claim);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/world-events/{accountId}", (string accountId) =>
        {
            try
            {
                return Results.Json(_worldEventStore.GetBoard(accountId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/world-events/upsert", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<WorldEventUpsertRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_worldEventStore.Upsert(request.Event));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/world-events/state", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<WorldEventStateRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.State))
            {
                return Results.BadRequest(new { error = "eventId and state are required." });
            }

            try
            {
                return Results.Json(_worldEventStore.SetState(request.EventId.Trim(), request.State.Trim()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/world-events/contribute", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<WorldEventContributionRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.AccountId))
            {
                return Results.BadRequest(new { error = "eventId and accountId are required." });
            }

            try
            {
                return Results.Json(_worldEventStore.AddContribution(request.EventId.Trim(), request.AccountId.Trim(), request.Amount));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/world-events/claim", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<WorldEventClaimRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.AccountId))
            {
                return Results.BadRequest(new { error = "eventId and accountId are required." });
            }

            try
            {
                var claim = _worldEventStore.Claim(request.EventId.Trim(), request.AccountId.Trim());
                if (claim.AwardedExperience > 0)
                {
                    _progressionStore.GrantExperience(claim.AccountId, "adventuring", claim.AwardedExperience);
                }

                if (!string.IsNullOrWhiteSpace(claim.AwardedReputationFaction) && claim.AwardedReputationAmount != 0)
                {
                    _reputationStore.Award(claim.AccountId, claim.AwardedReputationFaction, claim.AwardedReputationAmount, $"World event reward: {claim.EventId}", "world-event-system");
                }

                return Results.Json(claim);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/world-events/advance", () =>
        {
            try
            {
                return Results.Json(_worldEventStore.AdvanceLifecycle(DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/world-events/{eventId}/leaderboard", (string eventId, int limit = 20) =>
        {
            try
            {
                return Results.Json(_worldEventStore.GetLeaderboard(eventId, limit));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/invasions/{accountId}", (string accountId) =>
        {
            try
            {
                return Results.Json(_invasionStore.GetBoard(accountId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/invasions/upsert", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InvasionUpsertRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null)
            {
                return Results.BadRequest(new { error = "Invalid request." });
            }

            try
            {
                return Results.Json(_invasionStore.Upsert(request.Invasion));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/invasions/advance-wave", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InvasionAdvanceWaveRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.InvasionId))
            {
                return Results.BadRequest(new { error = "invasionId is required." });
            }

            try
            {
                return Results.Json(_invasionStore.AdvanceWave(request.InvasionId.Trim()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/invasions/record-kill", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InvasionRecordKillRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.InvasionId) || string.IsNullOrWhiteSpace(request.AccountId))
            {
                return Results.BadRequest(new { error = "invasionId and accountId are required." });
            }

            try
            {
                return Results.Json(_invasionStore.RecordKill(request.InvasionId.Trim(), request.AccountId.Trim(), request.Kills));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/invasions/claim", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<InvasionClaimRequest>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.InvasionId) || string.IsNullOrWhiteSpace(request.AccountId))
            {
                return Results.BadRequest(new { error = "invasionId and accountId are required." });
            }

            try
            {
                var claim = _invasionStore.Claim(request.InvasionId.Trim(), request.AccountId.Trim());
                _progressionStore.GrantExperience(claim.AccountId, "combat", claim.AwardedExperience);
                _reputationStore.Award(claim.AccountId, claim.AwardedReputationFaction, claim.AwardedReputationAmount, $"Invasion reward: {claim.InvasionId}", "invasion-system");
                return Results.Json(claim);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildHtml(), cancellationToken);
        });
        app.MapGet("/tools/items", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildDefinitionEditorPage("Items", "items"), cancellationToken);
        });
        app.MapGet("/tools", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildToolsHomePage(), cancellationToken);
        });
        app.MapGet("/tools/skills", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildDefinitionEditorPage("Skills", "skills"), cancellationToken);
        });
        app.MapGet("/tools/resources", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildDefinitionEditorPage("Resources", "resources"), cancellationToken);
        });
        app.MapGet("/tools/nodes", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildDefinitionEditorPage("Resource Nodes", "nodes"), cancellationToken);
        });
        app.MapGet("/tools/zones", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildDefinitionEditorPage("Zones", "zones"), cancellationToken);
        });
        app.MapGet("/tools/moderation", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildModerationPage(), cancellationToken);
        });
        app.MapGet("/tools/inventory", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildInventoryPage(), cancellationToken);
        });
        app.MapGet("/tools/crafting", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildCraftingPage(), cancellationToken);
        });
        app.MapGet("/tools/progression", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildProgressionPage(), cancellationToken);
        });
        app.MapGet("/tools/combat", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildCombatPage(), cancellationToken);
        });
        app.MapGet("/tools/reputation", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildReputationPage(), cancellationToken);
        });
        app.MapGet("/tools/quests", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildQuestPage(), cancellationToken);
        });
        app.MapGet("/tools/world-events", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildWorldEventPage(), cancellationToken);
        });
        app.MapGet("/tools/invasions", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildInvasionPage(), cancellationToken);
        });
        app.MapGet("/map", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildMapHtml(), cancellationToken);
        });

        await app.StartAsync(cancellationToken);
        Console.WriteLine($"Management dashboard listening on http://127.0.0.1:{_port}.");
        await app.WaitForShutdownAsync(cancellationToken);
    }

    private string BuildHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MMO Network Dashboard</title>
  <style>
    :root {
      --bg: #f4efe7;
      --panel: rgba(255,255,255,0.78);
      --panel-border: rgba(61,43,31,0.14);
      --text: #26170f;
      --muted: #6a5547;
      --accent: #a14d2d;
      --accent-soft: #f2d8b8;
      --good: #24613e;
      --warn: #a06018;
      --shadow: 0 18px 60px rgba(59,34,20,0.12);
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Palatino Linotype", serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, #f8e8ce 0, transparent 30%),
        radial-gradient(circle at bottom right, #e8c7ba 0, transparent 28%),
        linear-gradient(160deg, #f3ebdf 0%, #efe4d6 40%, #e7d7ca 100%);
      min-height: 100vh;
    }

    .wrap {
      width: min(1280px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 28px 0 36px;
    }

    .hero {
      display: grid;
      gap: 12px;
      margin-bottom: 22px;
    }

    h1 {
      margin: 0;
      font-size: clamp(2.1rem, 5vw, 4.2rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
      max-width: 10ch;
    }

    .sub {
      color: var(--muted);
      max-width: 70ch;
      font-size: 1rem;
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: 16px;
    }

    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(14px);
      padding: 18px;
    }

    .stat {
      grid-column: span 3;
      min-height: 132px;
      display: grid;
      align-content: space-between;
    }

    .wide {
      grid-column: span 6;
    }

    .full {
      grid-column: 1 / -1;
    }

    .eyebrow {
      color: var(--muted);
      font-size: 0.82rem;
      text-transform: uppercase;
      letter-spacing: 0.12em;
    }

    .value {
      font-size: clamp(1.8rem, 3vw, 3rem);
      line-height: 1;
      font-weight: 700;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      padding: 6px 10px;
      border-radius: 999px;
      font-size: 0.85rem;
      background: var(--accent-soft);
      color: var(--accent);
      width: fit-content;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 14px;
      font-size: 0.95rem;
    }

    th, td {
      padding: 10px 8px;
      text-align: left;
      border-bottom: 1px solid rgba(61,43,31,0.1);
      vertical-align: top;
    }

    th {
      color: var(--muted);
      font-weight: 600;
      font-size: 0.8rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }

    .zone-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
      gap: 14px;
      margin-top: 14px;
    }

    .zone {
      background: rgba(255,255,255,0.72);
      border: 1px solid rgba(61,43,31,0.1);
      border-radius: 18px;
      padding: 14px;
      display: grid;
      gap: 10px;
    }

    .zone h3 {
      margin: 0;
      font-size: 1.2rem;
    }

    .kv {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 6px 10px;
      color: var(--muted);
      font-size: 0.94rem;
    }

    .ok { color: var(--good); }
    .warn { color: var(--warn); }
    .editor-toolbar { display: flex; gap: 10px; margin-top: 12px; flex-wrap: wrap; }
    textarea {
      width: 100%;
      min-height: 280px;
      border-radius: 12px;
      border: 1px solid rgba(61,43,31,0.2);
      background: rgba(255,255,255,0.8);
      padding: 12px;
      font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 0.86rem;
      color: #26170f;
      resize: vertical;
    }
    button {
      border: 1px solid rgba(61,43,31,0.2);
      background: rgba(255,255,255,0.92);
      border-radius: 999px;
      padding: 8px 14px;
      color: #26170f;
      cursor: pointer;
    }

    @media (max-width: 920px) {
      .stat, .wide { grid-column: 1 / -1; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="hero">
      <div class="badge">Management Dashboard</div>
      <div class="badge"><a href="/map" style="color:inherit;text-decoration:none;">Open Map</a></div>
      <div class="badge"><a href="/tools/items" style="color:inherit;text-decoration:none;">Items Tool</a></div>
      <div class="badge"><a href="/tools" style="color:inherit;text-decoration:none;">All Tools</a></div>
      <div class="badge"><a href="/tools/skills" style="color:inherit;text-decoration:none;">Skills Tool</a></div>
      <div class="badge"><a href="/tools/resources" style="color:inherit;text-decoration:none;">Resources Tool</a></div>
      <div class="badge"><a href="/tools/nodes" style="color:inherit;text-decoration:none;">Nodes Tool</a></div>
      <div class="badge"><a href="/tools/zones" style="color:inherit;text-decoration:none;">Zones Tool</a></div>
      <div class="badge"><a href="/tools/moderation" style="color:inherit;text-decoration:none;">Moderation Tool</a></div>
      <div class="badge"><a href="/tools/inventory" style="color:inherit;text-decoration:none;">Inventory Tool</a></div>
      <div class="badge"><a href="/tools/crafting" style="color:inherit;text-decoration:none;">Crafting Tool</a></div>
      <div class="badge"><a href="/tools/progression" style="color:inherit;text-decoration:none;">Progression Tool</a></div>
      <div class="badge"><a href="/tools/combat" style="color:inherit;text-decoration:none;">Combat Tool</a></div>
      <div class="badge"><a href="/tools/reputation" style="color:inherit;text-decoration:none;">Reputation Tool</a></div>
      <div class="badge"><a href="/tools/quests" style="color:inherit;text-decoration:none;">Quest Tool</a></div>
      <div class="badge"><a href="/tools/world-events" style="color:inherit;text-decoration:none;">World Events</a></div>
      <div class="badge"><a href="/tools/invasions" style="color:inherit;text-decoration:none;">Invasions</a></div>
      <h1>Live zone and session control surface.</h1>
      <div class="sub">
        This view reflects the in-process MMO runtime. It is intended for basic operations now: gateway health, online sessions, zone populations, and transfer activity.
      </div>
    </section>

    <section class="grid">
      <article class="card stat">
        <div class="eyebrow">Sessions</div>
        <div class="value" id="sessionCount">0</div>
        <div id="lastUpdated" class="badge">Waiting for data</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Zones</div>
        <div class="value" id="zoneCount">0</div>
        <div class="badge">Realtime runtime view</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Gateway Logins</div>
        <div class="value" id="successfulLogins">0</div>
        <div class="badge">TCP 7000</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Transfers</div>
        <div class="value" id="transferCount">0</div>
        <div class="badge">Cross-zone handoffs</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Ghosts</div>
        <div class="value" id="ghostCount">0</div>
        <div class="badge">Adjacent-zone overlap</div>
      </article>

      <article class="card wide">
        <div class="eyebrow">Gateway</div>
        <table>
          <tbody>
            <tr><th>Port</th><td id="gatewayPort">-</td></tr>
            <tr><th>Connection Attempts</th><td id="gatewayAttempts">-</td></tr>
            <tr><th>Successful Logins</th><td id="gatewaySuccess">-</td></tr>
            <tr><th>Errors</th><td id="gatewayErrors">-</td></tr>
          </tbody>
        </table>
      </article>

      <article class="card wide">
        <div class="eyebrow">Zone Fleet</div>
        <div class="sub">AOI-filtered snapshots per player. Ghost replicas appear only near neighboring seams.</div>
        <div id="zones" class="zone-grid"></div>
      </article>

      <article class="card full">
        <div class="eyebrow">Sessions</div>
        <table>
          <thead>
            <tr>
              <th>Player</th>
              <th>Account</th>
              <th>Zone</th>
              <th>Position</th>
              <th>Pending Attachments</th>
            </tr>
          </thead>
          <tbody id="sessions"></tbody>
        </table>
      </article>

      <article class="card full">
        <div class="eyebrow">Gameplay Tools</div>
        <div class="sub">Use dedicated pages for each data domain so edits stay focused:</div>
        <ul>
          <li><a href="/tools/items">Items</a></li>
          <li><a href="/tools/skills">Skills</a></li>
          <li><a href="/tools/resources">Resources</a></li>
          <li><a href="/tools/nodes">Resource Nodes</a></li>
          <li><a href="/tools/zones">Zones</a></li>
          <li><a href="/tools/moderation">Moderation</a></li>
          <li><a href="/tools/inventory">Inventory</a></li>
          <li><a href="/tools/crafting">Crafting</a></li>
          <li><a href="/tools/progression">Progression</a></li>
          <li><a href="/tools/combat">Combat</a></li>
          <li><a href="/tools/reputation">Reputation</a></li>
          <li><a href="/tools/quests">Quests</a></li>
          <li><a href="/tools/world-events">World Events</a></li>
          <li><a href="/tools/invasions">Invasions</a></li>
        </ul>
      </article>
    </section>
  </div>

  <script>
    const fmt = new Intl.NumberFormat();

    function render(snapshot) {
      document.getElementById('sessionCount').textContent = fmt.format(snapshot.sessions.length);
      document.getElementById('zoneCount').textContent = fmt.format(snapshot.zones.length);
      document.getElementById('successfulLogins').textContent = fmt.format(snapshot.gateway.successfulLogins);
      document.getElementById('transferCount').textContent = fmt.format(snapshot.zones.reduce((sum, zone) => sum + zone.totalTransfersInitiated, 0));
      document.getElementById('ghostCount').textContent = fmt.format(snapshot.zones.reduce((sum, zone) => sum + zone.activeGhosts, 0));
      document.getElementById('lastUpdated').textContent = `Updated ${new Date(snapshot.generatedAtUtc).toLocaleTimeString()}`;

      document.getElementById('gatewayPort').textContent = snapshot.gateway.port;
      document.getElementById('gatewayAttempts').textContent = fmt.format(snapshot.gateway.connectionAttempts);
      document.getElementById('gatewaySuccess').textContent = fmt.format(snapshot.gateway.successfulLogins);
      document.getElementById('gatewayErrors').textContent = fmt.format(snapshot.gateway.errors);

      document.getElementById('zones').innerHTML = snapshot.zones.map(zone => `
        <section class="zone">
          <div>
            <div class="badge">Zone ${zone.zoneId}</div>
            <h3>${zone.name}</h3>
          </div>
            <div class="kv">
            <div>State</div><div>${zone.lifecycleState}</div>
            <div>Mode</div><div>${zone.runtimeMode}</div>
            <div>TCP</div><div>${zone.tcpPort}</div>
            <div>UDP</div><div>${zone.udpPort}</div>
            <div>Bounds</div><div>X ${zone.minX.toFixed(0)}-${zone.maxX.toFixed(0)} / Z ${zone.minZ.toFixed(0)}-${zone.maxZ.toFixed(0)}</div>
            <div>Tick</div><div>${fmt.format(zone.tick)}</div>
            <div>Players</div><div class="${zone.activePlayers > 0 ? 'ok' : 'warn'}">${fmt.format(zone.activePlayers)}</div>
            <div>Ghosts</div><div>${fmt.format(zone.activeGhosts)}</div>
            <div>Transfers</div><div>${fmt.format(zone.totalTransfersInitiated)}</div>
            <div>AOI Radius</div><div>${zone.aoiRadius.toFixed(1)}</div>
            <div>Ghost Margin</div><div>${zone.ghostMargin.toFixed(1)}</div>
            <div>Prewarm Margin</div><div>${zone.prewarmMargin.toFixed(1)}</div>
            <div>Transfer Inset</div><div>${zone.transferInset.toFixed(1)}</div>
            <div>Last Change</div><div>${new Date(zone.lastStateChangeUtc).toLocaleTimeString()}</div>
            <div>Idle Since</div><div>${zone.idleSinceUtc ? new Date(zone.idleSinceUtc).toLocaleTimeString() : 'Active'}</div>
          </div>
        </section>
      `).join('');

      document.getElementById('sessions').innerHTML = snapshot.sessions.map(session => {
        const pending = session.pendingAttachments.length === 0
          ? '<span class="warn">None</span>'
          : session.pendingAttachments.map(p => `Zone ${p.zoneId} @ (${p.spawnX.toFixed(1)}, ${p.spawnZ.toFixed(1)})`).join('<br>');

        return `
          <tr>
            <td>${session.playerId}<br><span class="eyebrow">${session.sessionId}</span></td>
            <td>${session.accountId}</td>
            <td>${session.currentZoneId}</td>
            <td>${session.positionX.toFixed(2)}, ${session.positionY.toFixed(2)}, ${session.positionZ.toFixed(2)}</td>
            <td>${pending}</td>
          </tr>
        `;
      }).join('');
    }

    async function refresh() {
      const response = await fetch('/api/dashboard', { cache: 'no-store' });
      const data = await response.json();
      render(data);
    }

    refresh();
    setInterval(refresh, 1500);
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildToolsHomePage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Gameplay Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1100px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.82); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); gap: 12px; }
    a.tool { display: grid; gap: 6px; text-decoration: none; color: #26170f; background: rgba(255,255,255,0.9); border: 1px solid rgba(61,43,31,0.15); border-radius: 14px; padding: 12px; }
    .muted { color: #6a5547; }
    code { background: rgba(0,0,0,0.04); padding: 1px 6px; border-radius: 8px; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Gameplay Data Tools</h1>
      <p class="muted">Choose a focused editor for each data domain. All tools persist through <code>/api/gameplay-definitions</code> into SQLite.</p>
      <p><a href="/">Back to Dashboard</a></p>
    </section>
    <section class="card grid">
      <a class="tool" href="/tools/items"><strong>Items</strong><span class="muted">Create stack rules, display names, and weights.</span></a>
      <a class="tool" href="/tools/skills"><strong>Skills</strong><span class="muted">Define skill IDs and progression caps.</span></a>
      <a class="tool" href="/tools/resources"><strong>Resources</strong><span class="muted">Map gatherable resource types to item output.</span></a>
      <a class="tool" href="/tools/nodes"><strong>Resource Nodes</strong><span class="muted">Place nodes in zones with respawn values.</span></a>
      <a class="tool" href="/tools/zones"><strong>Zones</strong><span class="muted">Edit world bounds metadata for design ops.</span></a>
      <a class="tool" href="/tools/moderation"><strong>Moderation</strong><span class="muted">Runtime account actions: mute, ban, kick, history.</span></a>
      <a class="tool" href="/tools/inventory"><strong>Inventory</strong><span class="muted">Load accounts and perform add/move/split/remove operations.</span></a>
      <a class="tool" href="/tools/crafting"><strong>Crafting</strong><span class="muted">Manage recipes and execute crafts against account inventory.</span></a>
      <a class="tool" href="/tools/progression"><strong>Progression</strong><span class="muted">Inspect and grant XP tracks (crafting, gathering, etc.).</span></a>
      <a class="tool" href="/tools/combat"><strong>Combat</strong><span class="muted">Spawn combatants and run attack simulations with logs.</span></a>
      <a class="tool" href="/tools/reputation"><strong>Reputation</strong><span class="muted">Track faction standing, tiers, and adjustment history.</span></a>
      <a class="tool" href="/tools/quests"><strong>Quests</strong><span class="muted">Create quest definitions and simulate progress/claim rewards.</span></a>
      <a class="tool" href="/tools/world-events"><strong>World Events</strong><span class="muted">Schedule event lifecycles, contributions, and reward claims.</span></a>
      <a class="tool" href="/tools/invasions"><strong>Invasions</strong><span class="muted">Run NPC mob invasion waves and player kill-credit rewards.</span></a>
      <a class="tool" href="/map"><strong>Operations Map</strong><span class="muted">Visualize sessions, movement, and transfers.</span></a>
    </section>
  </div>
</body>
</html>
""";

        return html;
    }

    private string BuildDefinitionEditorPage(string title, string section)
    {
        var exampleRow = section switch
        {
            "items" => """{"id":"fiber","name":"Plant Fiber","maxStack":200,"baseWeight":0.2}""",
            "skills" => """{"id":"carpentry","name":"Carpentry","maxValue":100}""",
            "resources" => """{"id":"hemp","name":"Hemp Plant","itemId":"fiber","baseYield":1}""",
            "nodes" => """{"id":"hemp-1","zoneId":1,"resourceId":"hemp","positionX":18,"positionY":0,"positionZ":63,"respawnSeconds":12}""",
            "zones" => """{"zoneId":3,"name":"EastField","minX":200,"maxX":300,"minZ":0,"maxZ":100}""",
            _ => "{}"
        };

        var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{title}} Tool</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1080px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.82); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    textarea { width: 100%; min-height: 520px; border-radius: 12px; border: 1px solid rgba(61,43,31,0.2); padding: 12px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.88rem; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; margin-right: 8px; }
    .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-top: 10px; }
    .status { color: #6a5547; }
    .hint { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>{{title}} Tool</h1>
      <p>Edit <strong>{{section}}</strong> in isolation. Save writes back through <code>/api/gameplay-definitions</code>.</p>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
        <a class="btn" href="/tools/items">Items</a>
        <a class="btn" href="/tools/skills">Skills</a>
        <a class="btn" href="/tools/resources">Resources</a>
        <a class="btn" href="/tools/nodes">Nodes</a>
        <a class="btn" href="/tools/zones">Zones</a>
        <a class="btn" href="/tools/moderation">Moderation</a>
        <a class="btn" href="/tools/inventory">Inventory</a>
        <a class="btn" href="/tools/crafting">Crafting</a>
        <a class="btn" href="/tools/progression">Progression</a>
        <a class="btn" href="/tools/combat">Combat</a>
      </div>
    </section>
    <section class="card">
      <div class="hint">Template row: <code id="templateRow">{{exampleRow}}</code></div>
      <textarea id="editor" spellcheck="false"></textarea>
      <div class="row">
        <button id="reload">Reload {{title}}</button>
        <button id="save">Save {{title}}</button>
        <button id="addTemplate">Add Template Row</button>
        <button id="formatJson">Format JSON</button>
        <span id="status" class="status">Loading...</span>
        <span id="count" class="status"></span>
      </div>
    </section>
  </div>
  <script>
    const section = '{{section}}';
    const templateRow = JSON.parse(document.getElementById('templateRow').textContent);
    const editor = document.getElementById('editor');
    const status = document.getElementById('status');
    const count = document.getElementById('count');

    function refreshCount() {
      try {
        const arr = JSON.parse(editor.value);
        count.textContent = Array.isArray(arr) ? `Rows: ${arr.length}` : 'Rows: invalid JSON root';
      } catch {
        count.textContent = 'Rows: invalid JSON';
      }
    }

    async function loadSection() {
      const response = await fetch('/api/gameplay-definitions', { cache: 'no-store' });
      const snapshot = await response.json();
      editor.value = JSON.stringify(snapshot[section] || [], null, 2);
      status.textContent = `Loaded ${section} at ${new Date().toLocaleTimeString()}`;
      refreshCount();
    }

    async function saveSection() {
      try {
        const sectionValue = JSON.parse(editor.value);
        const currentResponse = await fetch('/api/gameplay-definitions', { cache: 'no-store' });
        const current = await currentResponse.json();
        current[section] = sectionValue;
        const saveResponse = await fetch('/api/gameplay-definitions', {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(current)
        });
        const result = await saveResponse.json();
        if (!saveResponse.ok) {
          throw new Error(result.error || 'Save failed');
        }
        editor.value = JSON.stringify(result[section] || [], null, 2);
        status.textContent = `Saved ${section} at ${new Date().toLocaleTimeString()}`;
        refreshCount();
      } catch (err) {
        status.textContent = `Save failed: ${err.message}`;
      }
    }

    function addTemplateRow() {
      try {
        const current = JSON.parse(editor.value || '[]');
        if (!Array.isArray(current)) {
          throw new Error('Root JSON must be an array');
        }
        current.push(templateRow);
        editor.value = JSON.stringify(current, null, 2);
        refreshCount();
      } catch (err) {
        status.textContent = `Template insert failed: ${err.message}`;
      }
    }

    function formatJson() {
      try {
        editor.value = JSON.stringify(JSON.parse(editor.value), null, 2);
        refreshCount();
      } catch (err) {
        status.textContent = `Format failed: ${err.message}`;
      }
    }

    document.getElementById('reload').addEventListener('click', loadSection);
    document.getElementById('save').addEventListener('click', saveSection);
    document.getElementById('addTemplate').addEventListener('click', addTemplateRow);
    document.getElementById('formatJson').addEventListener('click', formatJson);
    editor.addEventListener('input', refreshCount);
    loadSection();
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildModerationPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Moderation Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1200px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    input, select { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 170px; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
    @media (max-width: 1024px) { .grid { grid-template-columns: 1fr; } }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Runtime Moderation</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
        <a class="btn" href="/tools/inventory">Inventory</a>
      </div>
      <p class="muted">Apply account moderation flags (mute/ban) and kick live sessions. Actions are logged in SQLite.</p>
    </section>
    <section class="card">
      <h2>Account Action</h2>
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <select id="accountAction">
          <option value="mute">Mute</option>
          <option value="ban">Ban</option>
          <option value="clear">Clear Flags</option>
        </select>
        <input id="accountReason" placeholder="Reason" />
        <input id="actor" placeholder="Actor" value="dashboard" />
        <button id="applyAccount">Apply</button>
      </div>
    </section>
    <section class="card">
      <h2>Kick Session</h2>
      <div class="row">
        <input id="kickSessionId" placeholder="sessionId (guid)" />
        <input id="kickReason" placeholder="Reason" />
        <button id="kickBtn">Kick</button>
      </div>
    </section>
    <section class="card grid">
      <div>
        <h3>Account Flags</h3>
        <table>
          <thead><tr><th>Account</th><th>State</th><th>Reason</th><th>Updated</th></tr></thead>
          <tbody id="accounts"></tbody>
        </table>
      </div>
      <div>
        <h3>Recent Actions</h3>
        <table>
          <thead><tr><th>Action</th><th>Target</th><th>Reason</th><th>When</th></tr></thead>
          <tbody id="actions"></tbody>
        </table>
      </div>
    </section>
    <section class="card">
      <div id="status" class="muted">Loading moderation data...</div>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(snapshot) {
      byId('accounts').innerHTML = (snapshot.accounts || []).map(a => `
        <tr>
          <td>${a.accountId}</td>
          <td>${a.isBanned ? 'BANNED' : a.isMuted ? 'MUTED' : 'ACTIVE'}</td>
          <td>${a.reason || ''}</td>
          <td>${new Date(a.updatedAtUtc).toLocaleString()}<br><span class="muted">${a.updatedBy || ''}</span></td>
        </tr>`).join('');

      byId('actions').innerHTML = (snapshot.recentActions || []).map(e => `
        <tr>
          <td>${e.actionType}</td>
          <td>${e.accountId || ''}<br><span class="muted">${e.sessionId || ''}</span></td>
          <td>${e.reason || ''}</td>
          <td>${new Date(e.createdAtUtc).toLocaleString()}<br><span class="muted">${e.actor || ''}</span></td>
        </tr>`).join('');
    }

    async function refreshModeration() {
      const response = await fetch('/api/moderation', { cache: 'no-store' });
      const snapshot = await response.json();
      render(snapshot);
      byId('status').textContent = `Moderation data updated ${new Date().toLocaleTimeString()}`;
    }

    async function applyAccountAction() {
      const accountId = byId('accountId').value.trim();
      const action = byId('accountAction').value;
      if (!accountId) {
        byId('status').textContent = 'accountId is required.';
        return;
      }

      const payload = {
        accountId,
        isMuted: action === 'mute',
        isBanned: action === 'ban',
        reason: byId('accountReason').value,
        actor: byId('actor').value || 'dashboard'
      };

      const response = await fetch('/api/moderation/account', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });

      const result = await response.json();
      if (!response.ok) {
        byId('status').textContent = result.error || 'Failed to apply moderation action.';
        return;
      }

      render(result);
      byId('status').textContent = `Applied ${action} to ${accountId}.`;
    }

    async function kickSession() {
      const sessionId = byId('kickSessionId').value.trim();
      if (!sessionId) {
        byId('status').textContent = 'sessionId is required.';
        return;
      }

      const payload = {
        sessionId,
        reason: byId('kickReason').value,
        actor: byId('actor').value || 'dashboard'
      };

      const response = await fetch('/api/moderation/kick', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const result = await response.json();
      if (!response.ok) {
        byId('status').textContent = result.error || 'Kick failed.';
        return;
      }

      render(result);
      byId('status').textContent = `Kick request submitted for ${sessionId}.`;
    }

    byId('applyAccount').addEventListener('click', applyAccountAction);
    byId('kickBtn').addEventListener('click', kickSession);
    refreshModeration();
    setInterval(refreshModeration, 3000);
  </script>
</body>
</html>
""";

        return html;
    }

    private sealed record ModerationAccountRequest(string AccountId, bool IsMuted, bool IsBanned, string? Reason, string? Actor);
    private sealed record ModerationKickRequest(Guid SessionId, string? Reason, string? Actor);
    private sealed record InventoryAddRequest(string AccountId, string ItemId, int Quantity, int MaxStack, int Capacity = 40);
    private sealed record InventoryMoveRequest(string AccountId, int FromSlot, int ToSlot, int Capacity = 40);
    private sealed record InventorySplitRequest(string AccountId, int FromSlot, int ToSlot, int Quantity, int Capacity = 40);
    private sealed record InventoryRemoveRequest(string AccountId, int SlotIndex, int Quantity, int Capacity = 40);
    private sealed record CraftingExecuteRequest(string AccountId, string RecipeId, int OutputMaxStack = 200, int Capacity = 40);
    private sealed record GrantProgressionRequest(string AccountId, string TrackId, int Experience);
    private sealed record CombatEnsureRequest(string AccountId);
    private sealed record CombatAttackRequest(string AttackerAccountId, string TargetAccountId, int BaseDamage = 10);
    private sealed record ReputationAwardRequest(string AccountId, string FactionId, int Amount, string? Reason, string? Actor);
    private sealed record QuestDefinitionUpsertRequest(QuestDefinitionSnapshot Definition, string? Actor);
    private sealed record QuestProgressRequest(string AccountId, string QuestId, int Amount = 1);
    private sealed record QuestActionRequest(string AccountId, string ActionId, int Amount = 1);
    private sealed record QuestClaimRequest(string AccountId, string QuestId);
    private sealed record WorldEventUpsertRequest(WorldEventSnapshot Event);
    private sealed record WorldEventStateRequest(string EventId, string State);
    private sealed record WorldEventContributionRequest(string EventId, string AccountId, int Amount = 1);
    private sealed record WorldEventClaimRequest(string EventId, string AccountId);
    private sealed record InvasionUpsertRequest(InvasionSnapshot Invasion);
    private sealed record InvasionAdvanceWaveRequest(string InvasionId);
    private sealed record InvasionRecordKillRequest(string InvasionId, string AccountId, int Kills = 1);
    private sealed record InvasionClaimRequest(string InvasionId, string AccountId);

    private string BuildInventoryPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Inventory Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1220px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    input { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 130px; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    .slots { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 8px; margin-top: 10px; }
    .slot { border: 1px solid rgba(61,43,31,0.16); border-radius: 12px; background: rgba(255,255,255,0.92); padding: 10px; min-height: 80px; }
    .empty { color: #6a5547; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Inventory Runtime Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
        <a class="btn" href="/tools/moderation">Moderation</a>
        <a class="btn" href="/tools/crafting">Crafting</a>
        <a class="btn" href="/tools/progression">Progression</a>
        <a class="btn" href="/tools/combat">Combat</a>
      </div>
      <p class="muted">Load an account inventory and perform add/move/split/remove operations persisted in SQLite.</p>
    </section>
    <section class="card">
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <input id="capacity" placeholder="capacity" value="40" />
        <button id="load">Load Inventory</button>
      </div>
    </section>
    <section class="card">
      <h2>Add Item</h2>
      <div class="row">
        <input id="addItemId" placeholder="itemId" />
        <input id="addQty" placeholder="qty" value="1" />
        <input id="addStack" placeholder="maxStack" value="200" />
        <button id="addBtn">Add</button>
      </div>
    </section>
    <section class="card">
      <h2>Move / Split / Remove</h2>
      <div class="row">
        <input id="fromSlot" placeholder="fromSlot" />
        <input id="toSlot" placeholder="toSlot" />
        <input id="opQty" placeholder="qty" value="1" />
        <button id="moveBtn">Move</button>
        <button id="splitBtn">Split</button>
        <button id="removeBtn">Remove</button>
      </div>
    </section>
    <section class="card">
      <div id="status" class="muted">Load an account to begin.</div>
      <div id="summary" class="muted"></div>
      <div id="slots" class="slots"></div>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }
    let currentInventory = null;

    function accountId() { return byId('accountId').value.trim(); }
    function capacity() { return parseInt(byId('capacity').value || '40', 10); }

    function renderInventory(snapshot) {
      currentInventory = snapshot;
      const slotMap = new Map((snapshot.slots || []).map(slot => [slot.slotIndex, slot]));
      const cards = [];
      for (let i = 0; i < snapshot.capacity; i++) {
        const slot = slotMap.get(i);
        if (!slot) {
          cards.push(`<div class="slot"><div class="muted">Slot ${i}</div><div class="empty">Empty</div></div>`);
        } else {
          cards.push(`<div class="slot"><div class="muted">Slot ${i}</div><div><strong>${slot.itemId}</strong></div><div>Qty: ${slot.quantity}</div></div>`);
        }
      }

      byId('slots').innerHTML = cards.join('');
      byId('summary').textContent = `Account: ${snapshot.accountId} | Slots used: ${(snapshot.slots || []).length}/${snapshot.capacity}`;
    }

    async function loadInventory() {
      if (!accountId()) {
        byId('status').textContent = 'accountId is required.';
        return;
      }

      const response = await fetch(`/api/inventory/${encodeURIComponent(accountId())}`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Load failed.';
        return;
      }

      data.capacity = capacity();
      renderInventory(data);
      byId('status').textContent = `Inventory loaded ${new Date().toLocaleTimeString()}`;
    }

    async function postInventory(path, payload) {
      const response = await fetch(path, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        throw new Error(data.error || 'Operation failed');
      }
      data.capacity = payload.capacity || capacity();
      renderInventory(data);
    }

    async function addItem() {
      await postInventory('/api/inventory/add', {
        accountId: accountId(),
        itemId: byId('addItemId').value,
        quantity: parseInt(byId('addQty').value || '1', 10),
        maxStack: parseInt(byId('addStack').value || '200', 10),
        capacity: capacity()
      });
      byId('status').textContent = 'Add operation complete.';
    }

    async function moveItem() {
      await postInventory('/api/inventory/move', {
        accountId: accountId(),
        fromSlot: parseInt(byId('fromSlot').value, 10),
        toSlot: parseInt(byId('toSlot').value, 10),
        capacity: capacity()
      });
      byId('status').textContent = 'Move operation complete.';
    }

    async function splitItem() {
      await postInventory('/api/inventory/split', {
        accountId: accountId(),
        fromSlot: parseInt(byId('fromSlot').value, 10),
        toSlot: parseInt(byId('toSlot').value, 10),
        quantity: parseInt(byId('opQty').value || '1', 10),
        capacity: capacity()
      });
      byId('status').textContent = 'Split operation complete.';
    }

    async function removeItem() {
      await postInventory('/api/inventory/remove', {
        accountId: accountId(),
        slotIndex: parseInt(byId('fromSlot').value, 10),
        quantity: parseInt(byId('opQty').value || '1', 10),
        capacity: capacity()
      });
      byId('status').textContent = 'Remove operation complete.';
    }

    byId('load').addEventListener('click', loadInventory);
    byId('addBtn').addEventListener('click', () => addItem().catch(err => byId('status').textContent = err.message));
    byId('moveBtn').addEventListener('click', () => moveItem().catch(err => byId('status').textContent = err.message));
    byId('splitBtn').addEventListener('click', () => splitItem().catch(err => byId('status').textContent = err.message));
    byId('removeBtn').addEventListener('click', () => removeItem().catch(err => byId('status').textContent = err.message));
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildCraftingPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Crafting Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1220px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    input, textarea { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); }
    textarea { width: 100%; min-height: 150px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Initial Crafting System</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
        <a class="btn" href="/tools/inventory">Inventory</a>
      </div>
      <p class="muted">Create/update recipes and execute crafting against account inventory using server-authoritative ingredient consumption + output creation.</p>
    </section>
    <section class="card">
      <h2>Recipe Editor</h2>
      <div class="muted">Recipe JSON example:</div>
      <textarea id="recipeJson">{\n  \"recipeId\": \"make-cloth\",\n  \"name\": \"Make Cloth\",\n  \"outputItemId\": \"cloth\",\n  \"outputQuantity\": 1,\n  \"craftSeconds\": 4,\n  \"ingredients\": [\n    { \"itemId\": \"fiber\", \"quantity\": 3 }\n  ]\n}</textarea>
      <div class="row">
        <button id="loadRecipes">Reload Recipes</button>
        <button id="saveRecipe">Upsert Recipe</button>
      </div>
    </section>
    <section class="card">
      <h2>Execute Craft</h2>
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <input id="recipeId" placeholder="recipeId" />
        <input id="capacity" placeholder="capacity" value="40" />
        <button id="craftBtn">Craft</button>
      </div>
      <div id="status" class="muted">Idle</div>
    </section>
    <section class="card">
      <h2>Recipes</h2>
      <table>
        <thead><tr><th>Recipe</th><th>Output</th><th>Ingredients</th></tr></thead>
        <tbody id="recipes"></tbody>
      </table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function renderRecipes(recipes) {
      byId('recipes').innerHTML = (recipes || []).map(recipe => `
        <tr>
          <td>${recipe.recipeId}<br><span class="muted">${recipe.name}</span></td>
          <td>${recipe.outputQuantity} x ${recipe.outputItemId}<br><span class="muted">${recipe.craftSeconds}s</span></td>
          <td>${(recipe.ingredients || []).map(i => `${i.quantity} x ${i.itemId}`).join('<br>')}</td>
        </tr>`).join('');
    }

    async function loadRecipes() {
      const response = await fetch('/api/crafting/recipes', { cache: 'no-store' });
      const data = await response.json();
      renderRecipes(data);
      byId('status').textContent = `Loaded ${data.length} recipes`;
    }

    async function saveRecipe() {
      const payload = JSON.parse(byId('recipeJson').value);
      const response = await fetch('/api/crafting/recipes/upsert', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Failed to save recipe';
        return;
      }

      byId('status').textContent = `Upserted recipe ${data.recipeId}`;
      loadRecipes();
    }

    async function craft() {
      const payload = {
        accountId: byId('accountId').value.trim(),
        recipeId: byId('recipeId').value.trim(),
        capacity: parseInt(byId('capacity').value || '40', 10)
      };
      const response = await fetch('/api/crafting/execute', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok || !data.success) {
        byId('status').textContent = data.message || data.error || 'Craft failed';
        return;
      }

      byId('status').textContent = data.message;
    }

    byId('loadRecipes').addEventListener('click', loadRecipes);
    byId('saveRecipe').addEventListener('click', () => saveRecipe().catch(err => byId('status').textContent = err.message));
    byId('craftBtn').addEventListener('click', () => craft().catch(err => byId('status').textContent = err.message));
    loadRecipes();
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildProgressionPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Progression Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1000px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    input { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 140px; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Progression Runtime Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
      </div>
      <p class="muted">Load progression tracks for an account and grant XP to simulate gameplay progression.</p>
    </section>
    <section class="card">
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <button id="loadBtn">Load Progression</button>
      </div>
      <div class="row">
        <input id="trackId" placeholder="trackId" value="crafting" />
        <input id="xp" placeholder="xp" value="25" />
        <button id="grantBtn">Grant XP</button>
      </div>
      <div id="status" class="muted">Idle</div>
      <table>
        <thead><tr><th>Track</th><th>Level</th><th>XP</th><th>To Next</th></tr></thead>
        <tbody id="tracks"></tbody>
      </table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(snapshot) {
      byId('tracks').innerHTML = (snapshot.tracks || []).map(track => `
        <tr>
          <td>${track.trackId}</td>
          <td>${track.level}</td>
          <td>${track.experience}</td>
          <td>${track.experienceToNextLevel}</td>
        </tr>`).join('');
    }

    async function load() {
      const accountId = byId('accountId').value.trim();
      if (!accountId) {
        byId('status').textContent = 'accountId is required.';
        return;
      }
      const response = await fetch(`/api/progression/${encodeURIComponent(accountId)}`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Load failed.';
        return;
      }
      render(data);
      byId('status').textContent = `Loaded progression for ${accountId}`;
    }

    async function grant() {
      const payload = {
        accountId: byId('accountId').value.trim(),
        trackId: byId('trackId').value.trim(),
        experience: parseInt(byId('xp').value || '0', 10)
      };
      const response = await fetch('/api/progression/grant', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Grant failed.';
        return;
      }
      render(data);
      byId('status').textContent = `Granted XP to ${payload.trackId}`;
    }

    byId('loadBtn').addEventListener('click', () => load().catch(err => byId('status').textContent = err.message));
    byId('grantBtn').addEventListener('click', () => grant().catch(err => byId('status').textContent = err.message));
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildCombatPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Combat Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1100px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    input { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 140px; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Combat Runtime Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
      </div>
      <p class="muted">Create combatants and run attack simulations to evolve gameplay systems.</p>
    </section>
    <section class="card">
      <div class="row">
        <input id="ensureAccount" placeholder="accountId" />
        <button id="ensureBtn">Ensure Combatant</button>
      </div>
      <div class="row">
        <input id="attacker" placeholder="attackerAccountId" />
        <input id="target" placeholder="targetAccountId" />
        <input id="damage" placeholder="baseDamage" value="10" />
        <button id="attackBtn">Attack</button>
      </div>
      <div id="status" class="muted">Idle</div>
    </section>
    <section class="card">
      <h2>Combatants</h2>
      <table><thead><tr><th>Account</th><th>HP</th><th>Stamina</th><th>Deaths</th></tr></thead><tbody id="combatants"></tbody></table>
      <h2>Recent Actions</h2>
      <table><thead><tr><th>Action</th><th>Attacker</th><th>Target</th><th>Damage</th><th>HP Left</th><th>Notes</th></tr></thead><tbody id="actions"></tbody></table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(snapshot) {
      byId('combatants').innerHTML = (snapshot.combatants || []).map(c => `
        <tr><td>${c.accountId}</td><td>${c.hitPoints}/${c.maxHitPoints}</td><td>${c.stamina}</td><td>${c.deaths}</td></tr>`).join('');
      byId('actions').innerHTML = (snapshot.recentActions || []).map(a => `
        <tr><td>${a.actionType}</td><td>${a.attackerAccountId}</td><td>${a.targetAccountId}</td><td>${a.damage}</td><td>${a.remainingHitPoints}</td><td>${a.notes}</td></tr>`).join('');
    }

    async function refresh() {
      const response = await fetch('/api/combat', { cache: 'no-store' });
      const data = await response.json();
      render(data);
    }

    async function ensureCombatant() {
      const payload = { accountId: byId('ensureAccount').value.trim() };
      const response = await fetch('/api/combat/ensure', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Ensure failed';
        return;
      }
      render(data);
      byId('status').textContent = `Ensured combatant ${payload.accountId}`;
    }

    async function attack() {
      const payload = {
        attackerAccountId: byId('attacker').value.trim(),
        targetAccountId: byId('target').value.trim(),
        baseDamage: parseInt(byId('damage').value || '10', 10)
      };
      const response = await fetch('/api/combat/attack', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Attack failed';
        return;
      }
      render(data);
      byId('status').textContent = `${payload.attackerAccountId} attacked ${payload.targetAccountId}`;
    }

    byId('ensureBtn').addEventListener('click', () => ensureCombatant().catch(err => byId('status').textContent = err.message));
    byId('attackBtn').addEventListener('click', () => attack().catch(err => byId('status').textContent = err.message));
    refresh();
    setInterval(refresh, 3000);
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildReputationPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Reputation Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1120px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    input { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 140px; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Faction Reputation Runtime Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
      </div>
      <p class="muted">Adjust per-faction reputation and inspect standing tiers/history for any account.</p>
    </section>
    <section class="card">
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <button id="loadBtn">Load Profile</button>
      </div>
      <div class="row">
        <input id="factionId" placeholder="factionId (e.g. wardens)" />
        <input id="amount" placeholder="amount" value="25" />
        <input id="reason" placeholder="reason" value="quest turn-in" />
        <button id="awardBtn">Apply Adjustment</button>
      </div>
      <div id="status" class="muted">Idle</div>
    </section>
    <section class="card">
      <h2>Faction Standings</h2>
      <table><thead><tr><th>Faction</th><th>Points</th><th>Tier</th></tr></thead><tbody id="factions"></tbody></table>
      <h2>Recent Adjustments</h2>
      <table><thead><tr><th>Faction</th><th>Amount</th><th>Reason</th><th>Actor</th><th>Time</th></tr></thead><tbody id="actions"></tbody></table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(profile) {
      byId('factions').innerHTML = (profile.factions || []).map(f => `
        <tr><td>${f.factionId}</td><td>${f.points}</td><td>${f.tier}</td></tr>`).join('');
      byId('actions').innerHTML = (profile.recentActions || []).map(a => `
        <tr><td>${a.factionId}</td><td>${a.amount}</td><td>${a.reason}</td><td>${a.actor}</td><td>${new Date(a.createdAtUtc).toLocaleString()}</td></tr>`).join('');
    }

    async function load() {
      const accountId = byId('accountId').value.trim();
      if (!accountId) {
        byId('status').textContent = 'accountId is required.';
        return;
      }
      const response = await fetch(`/api/reputation/${encodeURIComponent(accountId)}`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Load failed';
        return;
      }
      render(data);
      byId('status').textContent = `Loaded reputation for ${accountId}`;
    }

    async function award() {
      const payload = {
        accountId: byId('accountId').value.trim(),
        factionId: byId('factionId').value.trim(),
        amount: parseInt(byId('amount').value || '0', 10),
        reason: byId('reason').value.trim()
      };
      const response = await fetch('/api/reputation/award', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Adjustment failed';
        return;
      }
      render(data);
      byId('status').textContent = `Applied ${payload.amount} to ${payload.factionId}`;
    }

    byId('loadBtn').addEventListener('click', () => load().catch(err => byId('status').textContent = err.message));
    byId('awardBtn').addEventListener('click', () => award().catch(err => byId('status').textContent = err.message));
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildQuestPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Quest Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1160px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; margin-bottom: 10px; }
    input, textarea { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 140px; font-family: inherit; }
    textarea { width: 100%; min-height: 130px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.88rem; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Quest Runtime Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
      </div>
      <p class="muted">Author quest definitions, then simulate account progress and reward claims.</p>
    </section>
    <section class="card">
      <h2>Definition Upsert</h2>
      <p class="muted">JSON example: {"questId":"q_gather_hemp","title":"Hemp Harvest","description":"Collect hemp nodes","targetAction":"gather","targetCount":5,"rewardExperience":75,"rewardReputationFaction":"wardens","rewardReputationAmount":20}</p>
      <textarea id="definitionJson">{"questId":"q_gather_hemp","title":"Hemp Harvest","description":"Collect hemp nodes","targetAction":"gather","targetCount":5,"rewardExperience":75,"rewardReputationFaction":"wardens","rewardReputationAmount":20}</textarea>
      <div class="row">
        <button id="upsertBtn">Upsert Definition</button>
      </div>
    </section>
    <section class="card">
      <h2>Account Runtime</h2>
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <button id="loadBtn">Load Board</button>
      </div>
      <div class="row">
        <input id="questId" placeholder="questId" />
        <input id="amount" placeholder="progress amount" value="1" />
        <button id="progressBtn">Add Progress</button>
        <button id="claimBtn">Claim Rewards</button>
      </div>
      <div class="row">
        <input id="actionId" placeholder="actionId (e.g. gather)" />
        <input id="actionAmount" placeholder="action amount" value="1" />
        <button id="actionBtn">Apply Action Progress</button>
      </div>
      <div id="status" class="muted">Idle</div>
      <h3>Definitions</h3>
      <table><thead><tr><th>Quest</th><th>Target</th><th>Rewards</th></tr></thead><tbody id="definitions"></tbody></table>
      <h3>Account Progress</h3>
      <table><thead><tr><th>Quest</th><th>Progress</th><th>Completed</th><th>Claimed</th><th>Updated</th></tr></thead><tbody id="progress"></tbody></table>
      <h3>Recent Events</h3>
      <table><thead><tr><th>Quest</th><th>Type</th><th>Amount</th><th>Notes</th><th>Time</th></tr></thead><tbody id="events"></tbody></table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(board) {
      byId('definitions').innerHTML = (board.definitions || []).map(q => `
        <tr><td><strong>${q.questId}</strong><br>${q.title}<br><span class="muted">${q.description}</span></td><td>${q.targetAction} x${q.targetCount}</td><td>${q.rewardExperience} XP<br>${q.rewardReputationAmount} ${q.rewardReputationFaction || '(none)'}</td></tr>`).join('');
      byId('progress').innerHTML = (board.progress || []).map(p => `
        <tr><td>${p.questId}</td><td>${p.progressCount}</td><td>${p.isCompleted}</td><td>${p.isClaimed}</td><td>${new Date(p.updatedAtUtc).toLocaleString()}</td></tr>`).join('');
      byId('events').innerHTML = (board.recentEvents || []).map(e => `
        <tr><td>${e.questId}</td><td>${e.eventType}</td><td>${e.amount}</td><td>${e.notes}</td><td>${new Date(e.createdAtUtc).toLocaleString()}</td></tr>`).join('');
    }

    async function load() {
      const accountId = byId('accountId').value.trim();
      if (!accountId) {
        byId('status').textContent = 'accountId is required.';
        return;
      }
      const response = await fetch(`/api/quests/${encodeURIComponent(accountId)}`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Load failed';
        return;
      }
      render(data);
      byId('status').textContent = `Loaded board for ${accountId}`;
    }

    async function upsertDefinition() {
      let definition;
      try {
        definition = JSON.parse(byId('definitionJson').value);
      } catch (error) {
        byId('status').textContent = `Invalid JSON: ${error.message}`;
        return;
      }

      const response = await fetch('/api/quests/definitions/upsert', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ definition, actor: 'dashboard' })
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Upsert failed';
        return;
      }
      render(data);
      byId('status').textContent = `Upserted ${definition.questId}`;
    }

    async function progress() {
      const payload = {
        accountId: byId('accountId').value.trim(),
        questId: byId('questId').value.trim(),
        amount: parseInt(byId('amount').value || '1', 10)
      };
      const response = await fetch('/api/quests/progress', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Progress failed';
        return;
      }
      render(data);
      byId('status').textContent = `Added progress to ${payload.questId}`;
    }

    async function claim() {
      const payload = {
        accountId: byId('accountId').value.trim(),
        questId: byId('questId').value.trim()
      };
      const response = await fetch('/api/quests/claim', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Claim failed';
        return;
      }
      byId('status').textContent = data.message || `Claimed ${payload.questId}`;
      await load();
    }

    async function applyAction() {
      const payload = {
        accountId: byId('accountId').value.trim(),
        actionId: byId('actionId').value.trim(),
        amount: parseInt(byId('actionAmount').value || '1', 10)
      };
      const response = await fetch('/api/quests/action', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Action progress failed';
        return;
      }
      render(data);
      byId('status').textContent = `Applied ${payload.actionId} action progress`;
    }

    byId('loadBtn').addEventListener('click', () => load().catch(err => byId('status').textContent = err.message));
    byId('upsertBtn').addEventListener('click', () => upsertDefinition().catch(err => byId('status').textContent = err.message));
    byId('progressBtn').addEventListener('click', () => progress().catch(err => byId('status').textContent = err.message));
    byId('claimBtn').addEventListener('click', () => claim().catch(err => byId('status').textContent = err.message));
    byId('actionBtn').addEventListener('click', () => applyAction().catch(err => byId('status').textContent = err.message));
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildWorldEventPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>World Event Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1180px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; margin-bottom: 10px; }
    input, textarea { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 140px; font-family: inherit; }
    textarea { width: 100%; min-height: 120px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.88rem; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>World Event Runtime Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
      </div>
      <p class="muted">Schedule world events, track account contributions, and claim event rewards.</p>
    </section>
    <section class="card">
      <h2>Event Upsert</h2>
      <p class="muted">JSON example: {"eventId":"we_storm_001","title":"Stormfront Defense","description":"Defeat storm creatures across zones","state":"active","startsAtUtc":"2026-01-01T00:00:00Z","endsAtUtc":"2026-01-02T00:00:00Z","baseRewardExperience":200,"rewardReputationFaction":"wardens","rewardReputationAmount":35,"createdAtUtc":"2026-01-01T00:00:00Z"}</p>
      <textarea id="eventJson">{"eventId":"we_storm_001","title":"Stormfront Defense","description":"Defeat storm creatures across zones","state":"draft","startsAtUtc":"2026-01-01T00:00:00Z","endsAtUtc":"2026-01-02T00:00:00Z","baseRewardExperience":200,"rewardReputationFaction":"wardens","rewardReputationAmount":35,"createdAtUtc":"2026-01-01T00:00:00Z"}</textarea>
      <div class="row">
        <button id="upsertBtn">Upsert Event</button>
      </div>
    </section>
    <section class="card">
      <h2>Event Runtime</h2>
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <button id="loadBtn">Load Board</button>
      </div>
      <div class="row">
        <input id="eventId" placeholder="eventId" />
        <input id="state" placeholder="state (draft/active/resolved)" value="active" />
        <button id="setStateBtn">Set State</button>
      </div>
      <div class="row">
        <input id="contribution" placeholder="contribution amount" value="1" />
        <button id="contributeBtn">Add Contribution</button>
        <button id="claimBtn">Claim Event Rewards</button>
        <button id="tickBtn">Run Lifecycle Tick</button>
      </div>
      <div id="status" class="muted">Idle</div>
      <h3>Events</h3>
      <table><thead><tr><th>Event</th><th>State</th><th>Window</th><th>Rewards</th></tr></thead><tbody id="events"></tbody></table>
      <h3>Account Participation</h3>
      <table><thead><tr><th>Event</th><th>Contribution</th><th>Claimed</th><th>Updated</th></tr></thead><tbody id="participation"></tbody></table>
      <h3>Leaderboard (Selected Event)</h3>
      <table><thead><tr><th>Rank</th><th>Account</th><th>Contribution</th><th>Claimed</th></tr></thead><tbody id="leaderboard"></tbody></table>
      <h3>Recent Event Logs</h3>
      <table><thead><tr><th>Event</th><th>Account</th><th>Action</th><th>Details</th><th>Time</th></tr></thead><tbody id="logs"></tbody></table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(board) {
      byId('events').innerHTML = (board.events || []).map(e => `
        <tr><td><strong>${e.eventId}</strong><br>${e.title}<br><span class="muted">${e.description}</span></td><td>${e.state}</td><td>${new Date(e.startsAtUtc).toLocaleString()}<br>${new Date(e.endsAtUtc).toLocaleString()}</td><td>${e.baseRewardExperience} XP<br>${e.rewardReputationAmount} ${e.rewardReputationFaction || '(none)'}</td></tr>`).join('');
      byId('participation').innerHTML = (board.participation || []).map(p => `
        <tr><td>${p.eventId}</td><td>${p.contribution}</td><td>${p.isClaimed}</td><td>${new Date(p.updatedAtUtc).toLocaleString()}</td></tr>`).join('');
      byId('logs').innerHTML = (board.recentLogs || []).map(log => `
        <tr><td>${log.eventId}</td><td>${log.accountId}</td><td>${log.actionType}</td><td>${log.details}</td><td>${new Date(log.createdAtUtc).toLocaleString()}</td></tr>`).join('');
    }

    function renderLeaderboard(entries) {
      byId('leaderboard').innerHTML = (entries || []).map(row => `
        <tr><td>${row.rank}</td><td>${row.accountId}</td><td>${row.contribution}</td><td>${row.isClaimed}</td></tr>`).join('');
    }

    async function load() {
      const accountId = byId('accountId').value.trim();
      if (!accountId) {
        byId('status').textContent = 'accountId is required.';
        return;
      }
      const response = await fetch(`/api/world-events/${encodeURIComponent(accountId)}`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Load failed';
        return;
      }
      render(data);
      byId('status').textContent = `Loaded world events for ${accountId}`;
      await loadLeaderboard();
    }

    async function upsert() {
      let eventPayload;
      try {
        eventPayload = JSON.parse(byId('eventJson').value);
      } catch (error) {
        byId('status').textContent = `Invalid JSON: ${error.message}`;
        return;
      }
      const response = await fetch('/api/world-events/upsert', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ event: eventPayload })
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Upsert failed';
        return;
      }
      render(data);
      byId('status').textContent = `Upserted world event ${eventPayload.eventId}`;
    }

    async function setState() {
      const payload = {
        eventId: byId('eventId').value.trim(),
        state: byId('state').value.trim()
      };
      const response = await fetch('/api/world-events/state', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'State update failed';
        return;
      }
      render(data);
      byId('status').textContent = `Set ${payload.eventId} to ${payload.state}`;
      await loadLeaderboard();
    }

    async function contribute() {
      const payload = {
        eventId: byId('eventId').value.trim(),
        accountId: byId('accountId').value.trim(),
        amount: parseInt(byId('contribution').value || '1', 10)
      };
      const response = await fetch('/api/world-events/contribute', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Contribution failed';
        return;
      }
      render(data);
      byId('status').textContent = `Added contribution to ${payload.eventId}`;
      await loadLeaderboard();
    }

    async function claim() {
      const payload = {
        eventId: byId('eventId').value.trim(),
        accountId: byId('accountId').value.trim()
      };
      const response = await fetch('/api/world-events/claim', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Claim failed';
        return;
      }
      byId('status').textContent = data.message || `Claimed ${payload.eventId}`;
      await load();
    }

    async function runTick() {
      const response = await fetch('/api/world-events/advance', { method: 'POST' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Tick failed';
        return;
      }
      render(data);
      byId('status').textContent = 'Advanced world event lifecycle tick.';
      await loadLeaderboard();
    }

    async function loadLeaderboard() {
      const eventId = byId('eventId').value.trim();
      if (!eventId) {
        byId('leaderboard').innerHTML = '';
        return;
      }
      const response = await fetch(`/api/world-events/${encodeURIComponent(eventId)}/leaderboard?limit=20`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) {
        byId('status').textContent = data.error || 'Leaderboard load failed';
        return;
      }
      renderLeaderboard(data);
    }

    byId('loadBtn').addEventListener('click', () => load().catch(err => byId('status').textContent = err.message));
    byId('upsertBtn').addEventListener('click', () => upsert().catch(err => byId('status').textContent = err.message));
    byId('setStateBtn').addEventListener('click', () => setState().catch(err => byId('status').textContent = err.message));
    byId('contributeBtn').addEventListener('click', () => contribute().catch(err => byId('status').textContent = err.message));
    byId('claimBtn').addEventListener('click', () => claim().catch(err => byId('status').textContent = err.message));
    byId('tickBtn').addEventListener('click', () => runTick().catch(err => byId('status').textContent = err.message));
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildInvasionPage()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Invasion Tools</title>
  <style>
    body { margin: 0; font-family: Georgia, "Palatino Linotype", serif; background: #f3ebdf; color: #26170f; }
    .wrap { width: min(1180px, calc(100vw - 32px)); margin: 0 auto; padding: 24px 0 36px; display: grid; gap: 16px; }
    .card { background: rgba(255,255,255,0.85); border: 1px solid rgba(61,43,31,0.14); border-radius: 18px; padding: 16px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; margin-bottom: 10px; }
    input, textarea { padding: 8px 10px; border-radius: 10px; border: 1px solid rgba(61,43,31,0.2); min-width: 140px; font-family: inherit; }
    textarea { width: 100%; min-height: 120px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.88rem; }
    button, a.btn { display: inline-flex; align-items: center; border-radius: 999px; border: 1px solid rgba(61,43,31,0.2); background: #fff; color: #26170f; padding: 8px 14px; text-decoration: none; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.93rem; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid rgba(61,43,31,0.1); vertical-align: top; }
    .muted { color: #6a5547; font-size: 0.92rem; }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>NPC Mob Invasion Tools</h1>
      <div class="row">
        <a class="btn" href="/">Dashboard</a>
        <a class="btn" href="/tools">Tools Home</a>
      </div>
      <p class="muted">Schedule and run zone invasions with wave advancement, kill-credit tracking, and reward claims.</p>
    </section>
    <section class="card">
      <h2>Invasion Upsert</h2>
      <p class="muted">JSON example: {"invasionId":"inv_northfield_001","zoneId":1,"mobTypeId":"goblin_raider","totalWaves":4,"currentWave":0,"state":"planned","threatLevel":3,"startsAtUtc":"2026-01-01T00:00:00Z","endsAtUtc":"2026-01-02T00:00:00Z"}</p>
      <textarea id="invasionJson">{"invasionId":"inv_northfield_001","zoneId":1,"mobTypeId":"goblin_raider","totalWaves":4,"currentWave":0,"state":"planned","threatLevel":3,"startsAtUtc":"2026-01-01T00:00:00Z","endsAtUtc":"2026-01-02T00:00:00Z"}</textarea>
      <div class="row"><button id="upsertBtn">Upsert Invasion</button></div>
    </section>
    <section class="card">
      <h2>Invasion Runtime</h2>
      <div class="row">
        <input id="accountId" placeholder="accountId" />
        <button id="loadBtn">Load Board</button>
      </div>
      <div class="row">
        <input id="invasionId" placeholder="invasionId" />
        <button id="advanceBtn">Advance Wave</button>
      </div>
      <div class="row">
        <input id="kills" placeholder="kills" value="1" />
        <button id="recordKillBtn">Record Kill Credit</button>
        <button id="claimBtn">Claim Rewards</button>
      </div>
      <div id="status" class="muted">Idle</div>
      <h3>Active/Planned Invasions</h3>
      <table><thead><tr><th>Invasion</th><th>Zone</th><th>Mob</th><th>Wave</th><th>State</th><th>Threat</th></tr></thead><tbody id="invasions"></tbody></table>
      <h3>Account Kill Credits</h3>
      <table><thead><tr><th>Invasion</th><th>Kills</th><th>Claimed</th><th>Updated</th></tr></thead><tbody id="contrib"></tbody></table>
      <h3>Recent Invasion Logs</h3>
      <table><thead><tr><th>Invasion</th><th>Account</th><th>Action</th><th>Details</th><th>Time</th></tr></thead><tbody id="logs"></tbody></table>
    </section>
  </div>
  <script>
    function byId(id) { return document.getElementById(id); }

    function render(board) {
      byId('invasions').innerHTML = (board.invasions || []).map(i => `
        <tr><td>${i.invasionId}</td><td>${i.zoneId}</td><td>${i.mobTypeId}</td><td>${i.currentWave}/${i.totalWaves}</td><td>${i.state}</td><td>${i.threatLevel}</td></tr>`).join('');
      byId('contrib').innerHTML = (board.contributions || []).map(c => `
        <tr><td>${c.invasionId}</td><td>${c.kills}</td><td>${c.isClaimed}</td><td>${new Date(c.updatedAtUtc).toLocaleString()}</td></tr>`).join('');
      byId('logs').innerHTML = (board.recentLogs || []).map(log => `
        <tr><td>${log.invasionId}</td><td>${log.accountId}</td><td>${log.actionType}</td><td>${log.details}</td><td>${new Date(log.createdAtUtc).toLocaleString()}</td></tr>`).join('');
    }

    async function load() {
      const accountId = byId('accountId').value.trim();
      if (!accountId) { byId('status').textContent = 'accountId is required.'; return; }
      const response = await fetch(`/api/invasions/${encodeURIComponent(accountId)}`, { cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) { byId('status').textContent = data.error || 'Load failed'; return; }
      render(data);
      byId('status').textContent = `Loaded invasions for ${accountId}`;
    }

    async function upsert() {
      let invasion;
      try { invasion = JSON.parse(byId('invasionJson').value); } catch (error) {
        byId('status').textContent = `Invalid JSON: ${error.message}`; return;
      }
      const response = await fetch('/api/invasions/upsert', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ invasion })
      });
      const data = await response.json();
      if (!response.ok) { byId('status').textContent = data.error || 'Upsert failed'; return; }
      render(data);
      byId('status').textContent = `Upserted ${invasion.invasionId}`;
    }

    async function advanceWave() {
      const payload = { invasionId: byId('invasionId').value.trim() };
      const response = await fetch('/api/invasions/advance-wave', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) { byId('status').textContent = data.error || 'Advance failed'; return; }
      render(data);
      byId('status').textContent = `Advanced invasion wave for ${payload.invasionId}`;
    }

    async function recordKill() {
      const payload = {
        invasionId: byId('invasionId').value.trim(),
        accountId: byId('accountId').value.trim(),
        kills: parseInt(byId('kills').value || '1', 10)
      };
      const response = await fetch('/api/invasions/record-kill', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) { byId('status').textContent = data.error || 'Kill credit failed'; return; }
      render(data);
      byId('status').textContent = `Recorded kill credit for ${payload.accountId}`;
    }

    async function claim() {
      const payload = {
        invasionId: byId('invasionId').value.trim(),
        accountId: byId('accountId').value.trim()
      };
      const response = await fetch('/api/invasions/claim', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) { byId('status').textContent = data.error || 'Claim failed'; return; }
      byId('status').textContent = data.message || `Claimed ${payload.invasionId}`;
      await load();
    }

    byId('loadBtn').addEventListener('click', () => load().catch(err => byId('status').textContent = err.message));
    byId('upsertBtn').addEventListener('click', () => upsert().catch(err => byId('status').textContent = err.message));
    byId('advanceBtn').addEventListener('click', () => advanceWave().catch(err => byId('status').textContent = err.message));
    byId('recordKillBtn').addEventListener('click', () => recordKill().catch(err => byId('status').textContent = err.message));
    byId('claimBtn').addEventListener('click', () => claim().catch(err => byId('status').textContent = err.message));
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildMapHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MMO Network Map</title>
  <style>
    :root {
      --panel: rgba(255,255,255,0.82);
      --panel-border: rgba(61,43,31,0.14);
      --text: #26170f;
      --muted: #6a5547;
      --zone-fill: rgba(161,77,45,0.14);
      --zone-stroke: #a14d2d;
      --player-moving: #24613e;
      --player-idle: #a06018;
      --player-transfer: #c08b14;
      --player-ghost: #7d5ea8;
      --player-selected: #0a6c91;
      --trail: rgba(36,97,62,0.34);
      --transfer-line: rgba(160,96,24,0.72);
      --prewarm-fill: rgba(192,139,20,0.12);
      --event: rgba(10,108,145,0.84);
      --shadow: 0 18px 60px rgba(59,34,20,0.12);
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Palatino Linotype", serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, #f8e8ce 0, transparent 30%),
        radial-gradient(circle at bottom right, #e8c7ba 0, transparent 28%),
        linear-gradient(160deg, #f3ebdf 0%, #efe4d6 40%, #e7d7ca 100%);
      min-height: 100vh;
    }
    .wrap {
      width: min(1440px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 28px 0 36px;
      display: grid;
      gap: 16px;
    }
    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(14px);
      padding: 18px;
    }
    .hero {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: end;
      flex-wrap: wrap;
    }
    .title { display: grid; gap: 8px; }
    h1 {
      margin: 0;
      font-size: clamp(2rem, 4vw, 3.8rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
    }
    .sub { color: var(--muted); }
    .nav a {
      display: inline-flex;
      padding: 8px 12px;
      border-radius: 999px;
      border: 1px solid var(--panel-border);
      color: var(--text);
      text-decoration: none;
      margin-left: 8px;
      background: rgba(255,255,255,0.56);
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 360px;
      gap: 16px;
    }
    .map-frame {
      position: relative;
      min-height: 720px;
      overflow: hidden;
    }
    .toolbar {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      flex-wrap: wrap;
      margin-bottom: 14px;
      color: var(--muted);
      font-size: 0.95rem;
    }
    .toolbar-group {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
    }
    .toolbar label {
      display: inline-flex;
      gap: 8px;
      align-items: center;
    }
    #mapSvg {
      width: 100%;
      height: min(72vh, 820px);
      display: block;
      background: linear-gradient(180deg, rgba(255,255,255,0.5), rgba(255,255,255,0.2));
      border-radius: 18px;
    }
    .legend, .zone-list, .event-list, .player-detail { display: grid; gap: 10px; }
    .legend-item, .zone-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      color: var(--muted);
      font-size: 0.95rem;
    }
    .swatch {
      width: 14px;
      height: 14px;
      border-radius: 999px;
      display: inline-block;
      margin-right: 8px;
      vertical-align: middle;
    }
    .meta { color: var(--muted); font-size: 0.92rem; }
    .section-title {
      margin: 16px 0 8px;
      font-size: 0.86rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--muted);
    }
    .event-item, .detail-row {
      display: grid;
      gap: 4px;
      padding: 10px 12px;
      border-radius: 14px;
      background: rgba(255,255,255,0.54);
      border: 1px solid rgba(61,43,31,0.08);
    }
    .detail-grid {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 6px 10px;
      font-size: 0.93rem;
      color: var(--muted);
    }
    .empty { color: var(--muted); font-size: 0.92rem; padding: 8px 0; }
    @media (max-width: 1024px) {
      .layout { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card hero">
      <div class="title">
        <div class="sub">Operations Map</div>
        <h1>Zones, handoffs, and live player movement.</h1>
        <div class="sub">This view turns the runtime snapshot into a spatial debugging surface for seams, prewarm regions, and live transfers.</div>
      </div>
      <div class="nav">
        <a href="/">Dashboard</a>
        <a href="/map">Map</a>
      </div>
    </section>
    <section class="layout">
      <article class="card map-frame">
        <div class="toolbar">
          <div class="toolbar-group">
            <label><input id="toggleTrails" type="checkbox" checked> Trails</label>
            <label><input id="toggleEvents" type="checkbox" checked> Transfer events</label>
            <label><input id="toggleMargins" type="checkbox" checked> Boundary margins</label>
          </div>
          <div class="toolbar-group"><span>Refresh: 1.5s</span></div>
        </div>
        <svg id="mapSvg" viewBox="0 0 1200 720" preserveAspectRatio="xMidYMid meet"></svg>
      </article>
      <aside class="card">
        <div class="legend">
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-moving);"></span>Moving player</span><span id="movingCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-idle);"></span>Idle player</span><span id="idleCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-transfer);"></span>Transferring</span><span id="transferCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-ghost);"></span>Ghost / overlap</span><span id="ghostCount">0</span></div>
          <div class="legend-item"><span>Zones</span><span id="zoneCount">0</span></div>
          <div class="legend-item"><span>Updated</span><span id="updatedAt">-</span></div>
        </div>
        <div class="section-title">Player Detail</div>
        <div class="player-detail" id="playerDetail">
          <div class="empty">Click a player marker to inspect session, velocity, zone, and pending transfer.</div>
        </div>
        <div class="section-title">Recent Transfers</div>
        <div class="event-list" id="eventList">
          <div class="empty">No transfer events observed yet.</div>
        </div>
        <div class="section-title">Zones</div>
        <div class="zone-list" id="zoneList"></div>
      </aside>
    </section>
  </div>
  <script>
    const svg = document.getElementById('mapSvg');
    const toggleTrails = document.getElementById('toggleTrails');
    const toggleEvents = document.getElementById('toggleEvents');
    const toggleMargins = document.getElementById('toggleMargins');
    const playerDetail = document.getElementById('playerDetail');

    const trailHistory = new Map();
    const zoneHistory = new Map();
    const recentEvents = [];
    let selectedSessionId = null;
    let latestPlayers = [];

    function escapeHtml(value) {
      return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    function fmt(n) {
      return Number(n).toFixed(2);
    }

    function classifyPlayer(player) {
      const speed = Math.hypot(player.velocityX, player.velocityY, player.velocityZ);
      const hasPendingDestination = player.pendingDestinationZoneId != null;
      const isTransferring = hasPendingDestination && speed > 0.05;
      if (isTransferring) return { kind: 'transferring', color: '#c08b14', speed };
      if (hasPendingDestination) return { kind: 'ghost', color: '#7d5ea8', speed };
      if (speed > 0.05) return { kind: 'moving', color: '#24613e', speed };
      return { kind: 'idle', color: '#a06018', speed };
    }

    function pushTrail(player) {
      const key = player.sessionId;
      const existing = trailHistory.get(key) || [];
      existing.push({ x: player.positionX, z: player.positionZ, zoneId: player.zoneId, at: Date.now() });
      while (existing.length > 18) {
        existing.shift();
      }
      trailHistory.set(key, existing);
    }

    function pruneTrails(players) {
      const active = new Set(players.map(player => player.sessionId));
      for (const key of trailHistory.keys()) {
        if (!active.has(key)) {
          trailHistory.delete(key);
        }
      }
    }

    function registerZoneChanges(players) {
      const now = new Date();
      for (const player of players) {
        const previousZoneId = zoneHistory.get(player.sessionId);
        zoneHistory.set(player.sessionId, player.zoneId);
        if (previousZoneId != null && previousZoneId !== player.zoneId) {
          recentEvents.unshift({
            id: `${player.sessionId}:${now.getTime()}`,
            sessionId: player.sessionId,
            playerId: player.playerId,
            fromZoneId: previousZoneId,
            toZoneId: player.zoneId,
            x: player.positionX,
            z: player.positionZ,
            at: now.toISOString()
          });
        }
      }
      while (recentEvents.length > 12) {
        recentEvents.pop();
      }
    }

    function renderPlayerDetail() {
      if (!selectedSessionId) {
        playerDetail.innerHTML = '<div class="empty">Click a player marker to inspect session, velocity, zone, and pending transfer.</div>';
        return;
      }
      const player = latestPlayers.find(candidate => candidate.sessionId === selectedSessionId);
      if (!player) {
        playerDetail.innerHTML = '<div class="empty">Selected player is no longer present in the current snapshot.</div>';
        return;
      }
      const state = classifyPlayer(player);
      playerDetail.innerHTML = `
        <div class="detail-row">
          <div><strong>P${player.playerId}</strong> <span class="meta">${escapeHtml(state.kind)}</span></div>
          <div class="detail-grid">
            <span>Session</span><span>${escapeHtml(player.sessionId)}</span>
            <span>Zone</span><span>${player.zoneId}</span>
            <span>Position</span><span>${fmt(player.positionX)}, ${fmt(player.positionZ)}</span>
            <span>Velocity</span><span>${fmt(player.velocityX)}, ${fmt(player.velocityZ)}</span>
            <span>Pending dest</span><span>${player.pendingDestinationZoneId ?? '-'}</span>
          </div>
        </div>`;
    }

    function renderEvents() {
      const list = document.getElementById('eventList');
      if (recentEvents.length === 0) {
        list.innerHTML = '<div class="empty">No transfer events observed yet.</div>';
        return;
      }
      list.innerHTML = recentEvents.map(eventItem => `
        <div class="event-item">
          <div><strong>P${eventItem.playerId}</strong> moved ${eventItem.fromZoneId} → ${eventItem.toZoneId}</div>
          <div class="meta">${new Date(eventItem.at).toLocaleTimeString()} at ${fmt(eventItem.x)}, ${fmt(eventItem.z)}</div>
        </div>`).join('');
    }

    function render(snapshot) {
      const zones = snapshot.zones;
      if (zones.length === 0) {
        svg.innerHTML = '';
        return;
      }

      const world = zones.reduce((acc, zone) => ({
        minX: Math.min(acc.minX, zone.minX),
        maxX: Math.max(acc.maxX, zone.maxX),
        minZ: Math.min(acc.minZ, zone.minZ),
        maxZ: Math.max(acc.maxZ, zone.maxZ)
      }), { minX: Number.POSITIVE_INFINITY, maxX: Number.NEGATIVE_INFINITY, minZ: Number.POSITIVE_INFINITY, maxZ: Number.NEGATIVE_INFINITY });

      const pad = 36;
      const width = 1200;
      const height = 720;
      const innerWidth = width - pad * 2;
      const innerHeight = height - pad * 2;
      const worldWidth = Math.max(1, world.maxX - world.minX);
      const worldHeight = Math.max(1, world.maxZ - world.minZ);
      const scale = Math.min(innerWidth / worldWidth, innerHeight / worldHeight);
      const offsetX = pad + (innerWidth - worldWidth * scale) / 2;
      const offsetY = pad + (innerHeight - worldHeight * scale) / 2;
      const projectX = x => offsetX + (x - world.minX) * scale;
      const projectY = z => height - (offsetY + (z - world.minZ) * scale);

      let movingCount = 0;
      let idleCount = 0;
      let transferCount = 0;
      let ghostCount = 0;

      const flattenedPlayers = zones.flatMap(zone => zone.players.map(player => ({
        ...player,
        zoneId: zone.zoneId,
        zoneName: zone.name
      })));

      latestPlayers = flattenedPlayers;
      registerZoneChanges(flattenedPlayers);
      flattenedPlayers.forEach(pushTrail);
      pruneTrails(flattenedPlayers);

      const zoneRects = zones.map(zone => {
        const x = projectX(zone.minX);
        const y = projectY(zone.maxZ);
        const w = (zone.maxX - zone.minX) * scale;
        const h = (zone.maxZ - zone.minZ) * scale;
        const rightBoundaryX = projectX(zone.maxX);
        const prewarmStartX = zone.prewarmMargin > 0 ? projectX(Math.max(zone.minX, zone.maxX - zone.prewarmMargin)) : x;
        const prewarmWidth = zone.prewarmMargin > 0 ? Math.max(0, rightBoundaryX - prewarmStartX) : 0;
        return `
          <g>
            <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" fill="var(--zone-fill)" stroke="var(--zone-stroke)" stroke-width="3"></rect>
            ${toggleMargins.checked && prewarmWidth > 0 ? `<rect x="${prewarmStartX}" y="${y}" width="${prewarmWidth}" height="${h}" fill="var(--prewarm-fill)"></rect>` : ''}
            ${toggleMargins.checked ? `<line x1="${rightBoundaryX}" y1="${y}" x2="${rightBoundaryX}" y2="${y + h}" stroke="var(--transfer-line)" stroke-width="2.5" stroke-dasharray="8 8"></line>` : ''}
            <text x="${x + 16}" y="${y + 28}" font-size="20" fill="#26170f" font-weight="700">${escapeHtml(zone.name)} (${zone.zoneId})</text>
            <text x="${x + 16}" y="${y + 52}" font-size="15" fill="#6a5547">${escapeHtml(zone.lifecycleState)} · ${escapeHtml(zone.runtimeMode)}</text>
          </g>`;
      }).join('');

      const trails = toggleTrails.checked
        ? flattenedPlayers.map(player => {
            const points = trailHistory.get(player.sessionId) || [];
            if (points.length < 2) return '';
            const path = points.map(point => `${projectX(point.x)},${projectY(point.z)}`).join(' ');
            return `<polyline points="${path}" fill="none" stroke="var(--trail)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></polyline>`;
          }).join('')
        : '';

      const eventMarkers = toggleEvents.checked
        ? recentEvents.map(eventItem => `
            <g>
              <circle cx="${projectX(eventItem.x)}" cy="${projectY(eventItem.z)}" r="8" fill="none" stroke="var(--event)" stroke-width="3"></circle>
              <circle cx="${projectX(eventItem.x)}" cy="${projectY(eventItem.z)}" r="3" fill="var(--event)"></circle>
            </g>`).join('')
        : '';

      const playerDots = flattenedPlayers.map(player => {
        const state = classifyPlayer(player);
        if (state.kind === 'moving') movingCount++;
        if (state.kind === 'idle') idleCount++;
        if (state.kind === 'transferring') transferCount++;
        if (state.kind === 'ghost') ghostCount++;
        const x = projectX(player.positionX);
        const y = projectY(player.positionZ);
        const selected = selectedSessionId === player.sessionId;
        const headingScale = Math.max(12, Math.min(28, state.speed * 24));
        const divisor = Math.max(state.speed, 0.001);
        const dx = state.speed > 0.01 ? (player.velocityX / divisor) * headingScale : 0;
        const dy = state.speed > 0.01 ? (-player.velocityZ / divisor) * headingScale : 0;
        return `
          <g data-session-id="${player.sessionId}" style="cursor:pointer">
            <circle cx="${x}" cy="${y}" r="${selected ? 13 : 10}" fill="${state.color}" stroke="${selected ? '#0a6c91' : '#fffaf1'}" stroke-width="${selected ? 4 : 3}"></circle>
            ${state.speed > 0.05 ? `<line x1="${x}" y1="${y}" x2="${x + dx}" y2="${y + dy}" stroke="${state.color}" stroke-width="3" stroke-linecap="round"></line>` : ''}
            <text x="${x + 12}" y="${y - 12}" font-size="14" fill="#26170f">P${player.playerId}</text>
          </g>`;
      }).join('');

      const axes = `
        <g opacity="0.55">
          <text x="${pad}" y="${height - 10}" font-size="14" fill="#6a5547">X: ${world.minX.toFixed(0)} - ${world.maxX.toFixed(0)}</text>
          <text x="${width - 180}" y="${height - 10}" font-size="14" fill="#6a5547">Z: ${world.minZ.toFixed(0)} - ${world.maxZ.toFixed(0)}</text>
        </g>`;

      svg.innerHTML = `${zoneRects}${trails}${eventMarkers}${playerDots}${axes}`;

      document.getElementById('movingCount').textContent = movingCount;
      document.getElementById('idleCount').textContent = idleCount;
      document.getElementById('transferCount').textContent = transferCount;
      document.getElementById('ghostCount').textContent = ghostCount;
      document.getElementById('zoneCount').textContent = zones.length;
      document.getElementById('updatedAt').textContent = new Date(snapshot.generatedAtUtc).toLocaleTimeString();
      document.getElementById('zoneList').innerHTML = zones.map(zone => `
        <div class="zone-item">
          <span>Zone ${zone.zoneId} ${escapeHtml(zone.name)}</span>
          <span class="meta">${zone.players.length} players / ${zone.activeGhosts} ghosts</span>
        </div>
        <div class="meta">Bounds X ${zone.minX.toFixed(0)}-${zone.maxX.toFixed(0)} / Z ${zone.minZ.toFixed(0)}-${zone.maxZ.toFixed(0)}</div>
        <div class="meta">AOI ${zone.aoiRadius.toFixed(0)} / ghost margin ${zone.ghostMargin.toFixed(0)} / prewarm ${zone.prewarmMargin.toFixed(0)} / inset ${zone.transferInset.toFixed(0)}</div>`
      ).join('');

      svg.querySelectorAll('[data-session-id]').forEach(node => {
        node.addEventListener('click', event => {
          event.stopPropagation();
          selectedSessionId = node.getAttribute('data-session-id');
          renderPlayerDetail();
          render(snapshot);
        }, { once: true });
      });

      renderPlayerDetail();
      renderEvents();
    }

    toggleTrails.addEventListener('change', refresh);
    toggleEvents.addEventListener('change', refresh);
    toggleMargins.addEventListener('change', refresh);

    svg.addEventListener('click', event => {
      if (event.target === svg) {
        selectedSessionId = null;
        renderPlayerDetail();
      }
    });

    async function refresh() {
      try {
        const response = await fetch('/api/dashboard', { cache: 'no-store' });
        const data = await response.json();
        render(data);
      } catch (error) {
        console.error(error);
      }
    }

    refresh();
    setInterval(refresh, 1500);
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildMapHtmlLegacy()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MMO Network Map</title>
  <style>
    :root {
      --bg: #efe7da;
      --panel: rgba(255,255,255,0.82);
      --panel-border: rgba(61,43,31,0.14);
      --text: #26170f;
      --muted: #6a5547;
      --zone-fill: rgba(161,77,45,0.14);
      --zone-stroke: #a14d2d;
      --player: #24613e;
      --player-stopped: #a06018;
      --shadow: 0 18px 60px rgba(59,34,20,0.12);
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Palatino Linotype", serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, #f8e8ce 0, transparent 30%),
        radial-gradient(circle at bottom right, #e8c7ba 0, transparent 28%),
        linear-gradient(160deg, #f3ebdf 0%, #efe4d6 40%, #e7d7ca 100%);
      min-height: 100vh;
    }

    .wrap {
      width: min(1380px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 28px 0 36px;
      display: grid;
      gap: 16px;
    }

    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(14px);
      padding: 18px;
    }

    .hero {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: end;
      flex-wrap: wrap;
    }

    .title {
      display: grid;
      gap: 8px;
    }

    h1 {
      margin: 0;
      font-size: clamp(2rem, 4vw, 3.6rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
    }

    .sub { color: var(--muted); }
    .nav a {
      display: inline-flex;
      padding: 8px 12px;
      border-radius: 999px;
      border: 1px solid var(--panel-border);
      color: var(--text);
      text-decoration: none;
      margin-left: 8px;
      background: rgba(255,255,255,0.56);
    }

    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 320px;
      gap: 16px;
    }

    .map-frame {
      position: relative;
      min-height: 720px;
      overflow: hidden;
    }

    #mapSvg {
      width: 100%;
      height: min(72vh, 820px);
      display: block;
      background: linear-gradient(180deg, rgba(255,255,255,0.5), rgba(255,255,255,0.2));
      border-radius: 18px;
    }

    .legend, .zone-list {
      display: grid;
      gap: 10px;
    }

    .legend-item, .zone-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      color: var(--muted);
      font-size: 0.95rem;
    }

    .swatch {
      width: 14px;
      height: 14px;
      border-radius: 999px;
      display: inline-block;
      margin-right: 8px;
      vertical-align: middle;
    }

    .meta {
      color: var(--muted);
      font-size: 0.92rem;
    }

    @media (max-width: 1024px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card hero">
      <div class="title">
        <div class="sub">Operations Map</div>
        <h1>Zones, bounds, and live player positions.</h1>
        <div class="sub">This view projects the current dashboard snapshot into a 2D world map.</div>
      </div>
      <div class="nav">
        <a href="/">Dashboard</a>
        <a href="/map">Map</a>
      </div>
    </section>

    <section class="layout">
      <article class="card map-frame">
        <svg id="mapSvg" viewBox="0 0 1200 720" preserveAspectRatio="xMidYMid meet"></svg>
      </article>

      <aside class="card">
        <div class="legend">
          <div class="legend-item"><span><span class="swatch" style="background: var(--player);"></span>Moving player</span><span id="movingCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-stopped);"></span>Idle player</span><span id="idleCount">0</span></div>
          <div class="legend-item"><span>Zones</span><span id="zoneCount">0</span></div>
          <div class="legend-item"><span>Updated</span><span id="updatedAt">-</span></div>
        </div>
        <hr style="border:none;border-top:1px solid rgba(61,43,31,0.12);margin:16px 0;">
        <div class="zone-list" id="zoneList"></div>
      </aside>
    </section>
  </div>

  <script>
    const svg = document.getElementById('mapSvg');

    function escapeHtml(value) {
      return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    function render(snapshot) {
      const zones = snapshot.zones;
      if (zones.length === 0) {
        svg.innerHTML = '';
        return;
      }

      const world = zones.reduce((acc, zone) => ({
        minX: Math.min(acc.minX, zone.minX),
        maxX: Math.max(acc.maxX, zone.maxX),
        minZ: Math.min(acc.minZ, zone.minZ),
        maxZ: Math.max(acc.maxZ, zone.maxZ)
      }), { minX: Number.POSITIVE_INFINITY, maxX: Number.NEGATIVE_INFINITY, minZ: Number.POSITIVE_INFINITY, maxZ: Number.NEGATIVE_INFINITY });

      const pad = 36;
      const width = 1200;
      const height = 720;
      const innerWidth = width - pad * 2;
      const innerHeight = height - pad * 2;
      const worldWidth = Math.max(1, world.maxX - world.minX);
      const worldHeight = Math.max(1, world.maxZ - world.minZ);
      const scale = Math.min(innerWidth / worldWidth, innerHeight / worldHeight);
      const offsetX = pad + (innerWidth - worldWidth * scale) / 2;
      const offsetY = pad + (innerHeight - worldHeight * scale) / 2;
      const projectX = x => offsetX + (x - world.minX) * scale;
      const projectY = z => height - (offsetY + (z - world.minZ) * scale);

      let movingCount = 0;
      let idleCount = 0;

      const zoneRects = zones.map(zone => {
        const x = projectX(zone.minX);
        const y = projectY(zone.maxZ);
        const w = (zone.maxX - zone.minX) * scale;
        const h = (zone.maxZ - zone.minZ) * scale;
        return `
          <g>
            <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" fill="var(--zone-fill)" stroke="var(--zone-stroke)" stroke-width="3"></rect>
            <text x="${x + 16}" y="${y + 28}" font-size="20" fill="#26170f" font-weight="700">${escapeHtml(zone.name)} (${zone.zoneId})</text>
            <text x="${x + 16}" y="${y + 52}" font-size="15" fill="#6a5547">${escapeHtml(zone.lifecycleState)} · ${escapeHtml(zone.runtimeMode)}</text>
          </g>
        `;
      }).join('');

      const playerDots = zones.flatMap(zone => zone.players.map(player => {
        const speed = Math.abs(player.velocityX) + Math.abs(player.velocityY) + Math.abs(player.velocityZ);
        const moving = speed > 0.05;
        if (moving) movingCount++; else idleCount++;
        const x = projectX(player.positionX);
        const y = projectY(player.positionZ);
        const color = moving ? '#24613e' : '#a06018';
        return `
          <g>
            <circle cx="${x}" cy="${y}" r="9" fill="${color}" stroke="#fffaf1" stroke-width="3"></circle>
            <text x="${x + 12}" y="${y - 12}" font-size="14" fill="#26170f">P${player.playerId}</text>
          </g>
        `;
      })).join('');

      const axes = `
        <g opacity="0.55">
          <text x="${pad}" y="${height - 10}" font-size="14" fill="#6a5547">X: ${world.minX.toFixed(0)} - ${world.maxX.toFixed(0)}</text>
          <text x="${width - 180}" y="${height - 10}" font-size="14" fill="#6a5547">Z: ${world.minZ.toFixed(0)} - ${world.maxZ.toFixed(0)}</text>
        </g>
      `;

      svg.innerHTML = `${zoneRects}${playerDots}${axes}`;

      document.getElementById('movingCount').textContent = movingCount;
      document.getElementById('idleCount').textContent = idleCount;
      document.getElementById('zoneCount').textContent = zones.length;
      document.getElementById('updatedAt').textContent = new Date(snapshot.generatedAtUtc).toLocaleTimeString();
      document.getElementById('zoneList').innerHTML = zones.map(zone => `
        <div class="zone-item">
          <span>Zone ${zone.zoneId} ${escapeHtml(zone.name)}</span>
          <span class="meta">${zone.players.length} players</span>
        </div>
        <div class="meta">Bounds X ${zone.minX.toFixed(0)}-${zone.maxX.toFixed(0)} / Z ${zone.minZ.toFixed(0)}-${zone.maxZ.toFixed(0)}</div>
      `).join('');
    }

    async function refresh() {
      const response = await fetch('/api/dashboard', { cache: 'no-store' });
      const data = await response.json();
      render(data);
    }

    refresh();
    setInterval(refresh, 1500);
  </script>
</body>
</html>
""";

        return html;
    }
}
