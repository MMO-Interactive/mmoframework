using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;

namespace MonoGameEngine.Editor;

public sealed partial class RuleBasedEditorAssistant : IEditorAssistant
{
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = Color.Red,
        ["green"] = Color.Green,
        ["blue"] = Color.CornflowerBlue,
        ["orange"] = Color.Orange,
        ["purple"] = Color.MediumPurple,
        ["white"] = Color.White,
        ["yellow"] = Color.Yellow
    };

    public IReadOnlyList<EditorAction> Interpret(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Array.Empty<EditorAction>();
        }

        var normalized = prompt.Trim().ToLowerInvariant();
        var actions = new List<EditorAction>();

        if (TryExtractSpeed(normalized, out var speed))
        {
            actions.Add(new SetPlayerSpeedAction(speed));
        }

        if (TryExtractColor(normalized, out var color))
        {
            actions.Add(new SetPlayerColorAction(color));
        }

        if (TryExtractSpawn(normalized, out var markerPosition))
        {
            actions.Add(new SpawnMarkerAction(markerPosition));
        }

        if (TryExtractTerrainHeight(normalized, out var terrainHeight))
        {
            actions.Add(new SetTerrainHeightScaleAction(terrainHeight));
        }

        if (TryExtractZone(normalized, out var zoneId))
        {
            actions.Add(new SetZoneAction(zoneId));
        }

        if (TryExtractNpcSpawn(normalized, out var npcArchetype, out var npcLevel, out var npcPosition))
        {
            actions.Add(new AddNpcSpawnAction(npcArchetype, npcLevel, npcPosition));
        }

        if (TryExtractResourceSpawn(normalized, out var resourceType, out var resourcePosition))
        {
            actions.Add(new AddResourceSpawnAction(resourceType, resourcePosition));
        }

        if (TryExtractStructureBox(normalized, out var min, out var max, out var baseY, out var topY, out var materialId))
        {
            actions.Add(new PlaceStructureBoxAction(min, max, baseY, topY, materialId));
        }
        if (TryExtractSaveWorkspace(normalized, out var savePath))
        {
            actions.Add(new SaveWorkspaceAction(savePath));
        }

        if (TryExtractLoadWorkspace(normalized, out var loadPath))
        {
            actions.Add(new LoadWorkspaceAction(loadPath));
        }

        if (normalized.Contains("clear markers", StringComparison.Ordinal) || normalized.Contains("remove markers", StringComparison.Ordinal))
        {
            actions.Add(new ClearMarkersAction());
        }

        if (normalized.Contains("regenerate terrain", StringComparison.Ordinal)
            || normalized.Contains("refresh terrain", StringComparison.Ordinal)
            || normalized.Contains("new terrain", StringComparison.Ordinal))
        {
            actions.Add(new RegenerateTerrainAction());
        }

        return actions;
    }

    private static bool TryExtractSpeed(string prompt, out float speed)
    {
        speed = 0f;
        var match = SpeedRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out speed))
        {
            return false;
        }

        speed = Math.Clamp(speed, 40f, 800f);
        return true;
    }

    private static bool TryExtractTerrainHeight(string prompt, out float height)
    {
        height = 0f;
        var match = TerrainHeightRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out height))
        {
            return false;
        }

        height = Math.Clamp(height, 0.25f, 4f);
        return true;
    }

    private static bool TryExtractZone(string prompt, out string zoneId)
    {
        zoneId = string.Empty;
        var match = ZoneRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        zoneId = match.Groups[1].Value.Trim();
        return !string.IsNullOrWhiteSpace(zoneId);
    }

    private static bool TryExtractNpcSpawn(string prompt, out string npcArchetype, out int level, out Vector2 position)
    {
        npcArchetype = string.Empty;
        level = 1;
        position = Vector2.Zero;

        var match = NpcRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        npcArchetype = match.Groups[1].Value;

        if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out level))
        {
            level = 1;
        }

        level = Math.Clamp(level, 1, 120);

        if (!float.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            return false;
        }

        position = new Vector2(x, z);
        return true;
    }

    private static bool TryExtractResourceSpawn(string prompt, out string resourceType, out Vector2 position)
    {
        resourceType = string.Empty;
        position = Vector2.Zero;

        var match = ResourceRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        resourceType = match.Groups[1].Value;

        if (!float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            return false;
        }

        position = new Vector2(x, z);
        return true;
    }

    private static bool TryExtractStructureBox(string prompt, out Point min, out Point max, out int baseY, out int topY, out ushort materialId)
    {
        min = Point.Zero;
        max = Point.Zero;
        baseY = 0;
        topY = 5;
        materialId = 9;

        var match = StructureRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var x1)
            || !int.TryParse(match.Groups[2].Value, out var z1)
            || !int.TryParse(match.Groups[3].Value, out var x2)
            || !int.TryParse(match.Groups[4].Value, out var z2))
        {
            return false;
        }

        _ = int.TryParse(match.Groups[5].Value, out baseY);
        if (!int.TryParse(match.Groups[6].Value, out topY))
        {
            topY = baseY + 5;
        }

        if (ushort.TryParse(match.Groups[7].Value, out var parsedMaterial))
        {
            materialId = parsedMaterial;
        }

        min = new Point(Math.Min(x1, x2), Math.Min(z1, z2));
        max = new Point(Math.Max(x1, x2), Math.Max(z1, z2));
        return true;
    }


    private static bool TryExtractSaveWorkspace(string prompt, out string relativePath)
    {
        relativePath = string.Empty;
        var match = SaveRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        relativePath = match.Groups[1].Value.Trim();
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    private static bool TryExtractLoadWorkspace(string prompt, out string relativePath)
    {
        relativePath = string.Empty;
        var match = LoadRegex().Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        relativePath = match.Groups[1].Value.Trim();
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    private static bool TryExtractColor(string prompt, out Color color)
    {
        foreach (var entry in NamedColors)
        {
            if (prompt.Contains(entry.Key, StringComparison.Ordinal))
            {
                color = entry.Value;
                return true;
            }
        }

        color = Color.White;
        return false;
    }

    private static bool TryExtractSpawn(string prompt, out Vector2 position)
    {
        position = Vector2.Zero;

        if (!(prompt.Contains("spawn", StringComparison.Ordinal) || prompt.Contains("add marker", StringComparison.Ordinal)))
        {
            return false;
        }

        var match = SpawnRegex().Match(prompt);
        if (match.Success
            && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            position = new Vector2(x, y);
            return true;
        }

        position = new Vector2(640f, 360f);
        return true;
    }

    [GeneratedRegex(@"(?:speed|move speed|player speed)\s*(?:to|=)?\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex SpeedRegex();

    [GeneratedRegex(@"(?:terrain height|height scale|terrain scale)\s*(?:to|=)?\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex TerrainHeightRegex();

    [GeneratedRegex(@"(?:zone|set zone)\s+([a-z0-9_\-]+)", RegexOptions.Compiled)]
    private static partial Regex ZoneRegex();

    [GeneratedRegex(@"(?:add|spawn)\s+npc\s+([a-z0-9_\-]+)\s+level\s+(\d+)\s+at\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex NpcRegex();

    [GeneratedRegex(@"add\s+resource\s+([a-z0-9_\-]+)\s+at\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex ResourceRegex();

    [GeneratedRegex(@"(?:build|place)\s+box\s+(-?\d+)\s*[, ]\s*(-?\d+)\s*[, ]\s*(-?\d+)\s*[, ]\s*(-?\d+)(?:\s+base\s+(-?\d+))?(?:\s+top\s+(-?\d+))?(?:\s+material\s+(\d+))?", RegexOptions.Compiled)]
    private static partial Regex StructureRegex();

    [GeneratedRegex(@"save\s+(?:workspace|zone)\s+([a-z0-9_./\-]+)", RegexOptions.Compiled)]
    private static partial Regex SaveRegex();

    [GeneratedRegex(@"load\s+(?:workspace|zone)\s+([a-z0-9_./\-]+)", RegexOptions.Compiled)]
    private static partial Regex LoadRegex();

    [GeneratedRegex(@"(?:at|to)?\s*\(?\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*\)?", RegexOptions.Compiled)]
    private static partial Regex SpawnRegex();
}
