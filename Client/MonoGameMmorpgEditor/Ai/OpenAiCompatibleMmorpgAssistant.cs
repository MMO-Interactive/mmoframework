using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public sealed class OpenAiCompatibleMmorpgAssistant : IGenerativeMmorpgAssistant
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly IEditorAssistant _fallbackAssistant;
    private readonly HttpClient _httpClient = new();

    public OpenAiCompatibleMmorpgAssistant(Uri endpoint, string apiKey, string model, IEditorAssistant fallbackAssistant)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _model = model;
        _fallbackAssistant = fallbackAssistant;
    }

    public string DisplayName => "Generative MMORPG Assistant";

    public async Task<AssistantInterpretation> InterpretAsync(
        string prompt,
        MmorpgEditorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new ChatCompletionRequest(
                _model,
                new[]
                {
                    new ChatMessage("system", BuildSystemPrompt()),
                    new ChatMessage("user", BuildUserPrompt(prompt, context))
                },
                Temperature: 0.2f);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return AssistantInterpretation.FromActions(_fallbackAssistant.Interpret(prompt));
            }

            var actions = MmorpgEditorJsonActionParser.Parse(content);
            var message = MmorpgEditorJsonActionParser.TryParseMessage(content);
            return new AssistantInterpretation(actions, string.IsNullOrWhiteSpace(message) ? content.Trim() : message.Trim());
        }
        catch
        {
            return AssistantInterpretation.FromActions(_fallbackAssistant.Interpret(prompt));
        }
    }

    private static string BuildSystemPrompt()
    {
        return """
You are an AI-first editor assistant for an MMORPG world editor.
Return only strict JSON. Do not include markdown.
Your job is to turn natural language into editor actions.

Allowed JSON format:
{
  "message": "Short user-facing explanation with MMORPG worldbuilding attitude.",
  "actions": [
    { "type": "set_zone", "zoneId": "starter_zone" },
    { "type": "add_npc_spawn", "npcArchetype": "wolf", "level": 5, "x": 120, "z": 80 },
    { "type": "add_resource_spawn", "resourceType": "iron_vein", "x": 220, "z": 144 },
    { "type": "place_structure_box", "x1": 20, "z1": 20, "x2": 32, "z2": 32, "baseY": 2, "topY": 10, "materialId": 14 },
    { "type": "set_terrain_height_scale", "heightScale": 1.4 },
    { "type": "regenerate_terrain" },
    { "type": "save_workspace", "relativePath": "zones/starter_zone" },
    { "type": "load_workspace", "relativePath": "zones/starter_zone" }
  ]
}

Only create actions relevant to MMORPG development: zones, spawns, terrain, voxel structures, save/load.
Always include a short message, even when actions are applied.
Use flavorful MMORPG worldbuilding attitude, but keep it concise and practical.
Clamp NPC levels between 1 and 120.
Use X/Z coordinates for world positions.
""";
    }

    private static string BuildUserPrompt(string prompt, MmorpgEditorContext context)
    {
        return string.Create(CultureInfo.InvariantCulture, $"""
Current editor context:
- Zone: {context.ActiveZone.ZoneId}
- Zone size: {context.ActiveZone.WidthTiles} x {context.ActiveZone.HeightTiles} tiles
- NPC spawns: {context.NpcSpawnCount}
- Resource spawns: {context.ResourceSpawnCount}
- Terrain height scale: {context.TerrainHeightScale}

User request:
{prompt}
""");
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        float Temperature);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);
}
