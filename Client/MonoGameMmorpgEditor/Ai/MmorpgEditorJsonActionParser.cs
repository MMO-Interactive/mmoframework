using System.Text.Json;
using Microsoft.Xna.Framework;
using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public static class MmorpgEditorJsonActionParser
{
    public static string? TryParseMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJsonObject(json));
            return document.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<EditorAction> Parse(string json)
    {
        using var document = JsonDocument.Parse(ExtractJsonObject(json));
        if (!document.RootElement.TryGetProperty("actions", out var actionsElement)
            || actionsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<EditorAction>();
        }

        var actions = new List<EditorAction>();
        foreach (var actionElement in actionsElement.EnumerateArray())
        {
            if (!TryGetString(actionElement, "type", out var type))
            {
                continue;
            }

            var action = type switch
            {
                "set_zone" => ParseSetZone(actionElement),
                "add_npc_spawn" => ParseAddNpcSpawn(actionElement),
                "add_resource_spawn" => ParseAddResourceSpawn(actionElement),
                "place_structure_box" => ParsePlaceStructureBox(actionElement),
                "set_terrain_height_scale" => ParseSetTerrainHeightScale(actionElement),
                "regenerate_terrain" => new RegenerateTerrainAction(),
                "save_workspace" => ParseSaveWorkspace(actionElement),
                "load_workspace" => ParseLoadWorkspace(actionElement),
                _ => null
            };

            if (action != null)
            {
                actions.Add(action);
            }
        }

        return actions;
    }

    private static EditorAction? ParseSetZone(JsonElement element)
        => TryGetString(element, "zoneId", out var zoneId) ? new SetZoneAction(zoneId) : null;

    private static EditorAction? ParseAddNpcSpawn(JsonElement element)
    {
        if (!TryGetString(element, "npcArchetype", out var npcArchetype))
        {
            return null;
        }

        var level = Math.Clamp(GetInt(element, "level", 1), 1, 120);
        return new AddNpcSpawnAction(npcArchetype, level, new Vector2(GetFloat(element, "x"), GetFloat(element, "z")));
    }

    private static EditorAction? ParseAddResourceSpawn(JsonElement element)
        => TryGetString(element, "resourceType", out var resourceType)
            ? new AddResourceSpawnAction(resourceType, new Vector2(GetFloat(element, "x"), GetFloat(element, "z")))
            : null;

    private static EditorAction ParsePlaceStructureBox(JsonElement element)
    {
        var min = new Point(
            Math.Min(GetInt(element, "x1"), GetInt(element, "x2")),
            Math.Min(GetInt(element, "z1"), GetInt(element, "z2")));
        var max = new Point(
            Math.Max(GetInt(element, "x1"), GetInt(element, "x2")),
            Math.Max(GetInt(element, "z1"), GetInt(element, "z2")));

        var baseY = GetInt(element, "baseY");
        var topY = GetInt(element, "topY", baseY + 5);
        if (topY < baseY)
        {
            (baseY, topY) = (topY, baseY);
        }

        var materialId = (ushort)Math.Clamp(GetInt(element, "materialId", 9), 1, ushort.MaxValue);
        return new PlaceStructureBoxAction(min, max, baseY, topY, materialId);
    }

    private static EditorAction ParseSetTerrainHeightScale(JsonElement element)
        => new SetTerrainHeightScaleAction(Math.Clamp(GetFloat(element, "heightScale", 1f), 0.25f, 4f));

    private static EditorAction? ParseSaveWorkspace(JsonElement element)
        => TryGetString(element, "relativePath", out var relativePath) ? new SaveWorkspaceAction(relativePath) : null;

    private static EditorAction? ParseLoadWorkspace(JsonElement element)
        => TryGetString(element, "relativePath", out var relativePath) ? new LoadWorkspaceAction(relativePath) : null;

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start ? text[start..(end + 1)] : text;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int GetInt(JsonElement element, string propertyName, int fallback = 0)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return fallback;
    }

    private static float GetFloat(JsonElement element, string propertyName, float fallback = 0f)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out var number))
        {
            return number;
        }

        return fallback;
    }
}
