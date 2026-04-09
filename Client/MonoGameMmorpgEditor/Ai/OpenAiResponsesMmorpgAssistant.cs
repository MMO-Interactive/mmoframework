using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public sealed class OpenAiResponsesMmorpgAssistant : IGenerativeMmorpgAssistant
{
    private static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/responses");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _reasoningEffort;
    private readonly IEditorAssistant _fallbackAssistant;
    private readonly HttpClient _httpClient = new();

    public OpenAiResponsesMmorpgAssistant(
        string apiKey,
        string model,
        string reasoningEffort,
        IEditorAssistant fallbackAssistant,
        Uri? endpoint = null)
    {
        _endpoint = endpoint ?? DefaultEndpoint;
        _apiKey = apiKey;
        _model = model;
        _reasoningEffort = reasoningEffort;
        _fallbackAssistant = fallbackAssistant;
    }

    public string DisplayName => $"OpenAI Responses: {_model}";

    public async Task<AssistantInterpretation> InterpretAsync(
        string prompt,
        MmorpgEditorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new ResponsesRequest(
                _model,
                BuildInput(prompt, context),
                new ReasoningOptions(_reasoningEffort));

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var content = ExtractText(responseJson);
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

    private static string BuildInput(string prompt, MmorpgEditorContext context)
        => string.Create(CultureInfo.InvariantCulture, $$"""
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

Current editor context:
- Zone: {{context.ActiveZone.ZoneId}}
- Zone size: {{context.ActiveZone.WidthTiles}} x {{context.ActiveZone.HeightTiles}} tiles
- NPC spawns: {{context.NpcSpawnCount}}
- Resource spawns: {{context.ResourceSpawnCount}}
- Terrain height scale: {{context.TerrainHeightScale}}

User request:
{{prompt}}
""");

    private static string? ExtractText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!document.RootElement.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private sealed record ResponsesRequest(string Model, string Input, ReasoningOptions Reasoning);

    private sealed record ReasoningOptions(string Effort);
}
