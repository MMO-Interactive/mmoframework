using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorLiveCatalogCache
{
    private static LiveGameplayDefinitionsSnapshot _definitions;
    private static LiveDashboardSnapshot _dashboard;
    private static bool _refreshInFlight;
    private static string _status = "Catalog not loaded.";
    private static DateTime _lastRefreshUtc = DateTime.MinValue;

    public static LiveGameplayDefinitionsSnapshot Definitions => _definitions;
    public static LiveDashboardSnapshot Dashboard => _dashboard;
    public static bool IsRefreshing => _refreshInFlight;
    public static string Status => _status;
    public static DateTime LastRefreshUtc => _lastRefreshUtc;

    public static void EnsureWarm()
    {
        if (_definitions == null && !_refreshInFlight)
        {
            _ = RefreshAsync();
        }
    }

    public static async System.Threading.Tasks.Task RefreshAsync()
    {
        if (_refreshInFlight)
        {
            return;
        }

        _refreshInFlight = true;
        _status = "Refreshing live catalog...";
        try
        {
            var baseUrl = EditorPrefs.GetString("RiseOfHeroes.WorldEditor.ServerBaseUrl", "http://127.0.0.1:7080");
            _definitions = await WorldEditorServerClient.GetGameplayDefinitionsAsync(baseUrl);
            _dashboard = await WorldEditorServerClient.GetDashboardAsync(baseUrl);
            _lastRefreshUtc = DateTime.UtcNow;
            _status = "Catalog loaded from " + WorldEditorServerClient.NormalizeBaseUrl(baseUrl) + ".";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        finally
        {
            _refreshInFlight = false;
            InternalEditorUtilityRepaintAll();
        }
    }

    public static string[] GetSpawnTags()
        => (_definitions?.playerSpawns ?? Array.Empty<LivePlayerSpawnDefinition>())
            .Select(entry => entry.spawnTag)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static (string id, string label)[] GetResourceOptions()
        => (_definitions?.resources ?? Array.Empty<LiveResourceDefinition>())
            .Select(resource => (resource.id ?? string.Empty, string.IsNullOrWhiteSpace(resource.name) ? resource.id ?? string.Empty : resource.name + " (" + resource.id + ")"))
            .Where(entry => entry.Item1.Length > 0)
            .OrderBy(entry => entry.Item2, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string[] GetMobTypeOptions()
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mobSpawns = _definitions?.mobSpawns ?? Array.Empty<LiveMobSpawnDefinition>();
        for (var i = 0; i < mobSpawns.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(mobSpawns[i].mobTypeId))
            {
                values.Add(mobSpawns[i].mobTypeId);
            }
        }

        var zones = _dashboard?.zones ?? Array.Empty<LiveZoneRuntimeSnapshot>();
        for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
        {
            var mobs = zones[zoneIndex].mobs ?? Array.Empty<LiveZoneMobSnapshot>();
            for (var mobIndex = 0; mobIndex < mobs.Length; mobIndex++)
            {
                if (!string.IsNullOrWhiteSpace(mobs[mobIndex].mobTypeId))
                {
                    values.Add(mobs[mobIndex].mobTypeId);
                }
            }
        }

        if (values.Count == 0)
        {
            values.Add("wolf");
            values.Add("boar");
        }

        return values.OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static LiveNpcDefinition[] GetNpcTemplates(int zoneId)
    {
        var npcs = _definitions?.npcs ?? Array.Empty<LiveNpcDefinition>();
        var filtered = npcs.Where(entry => zoneId <= 0 || entry.zoneId == zoneId).ToArray();
        return filtered.Length > 0 ? filtered : npcs;
    }

    public static string BuildStatusLabel()
    {
        var sections = new List<string> { _status };
        if (_lastRefreshUtc != DateTime.MinValue)
        {
            sections.Add("Updated " + _lastRefreshUtc.ToLocalTime().ToString("T"));
        }

        var definitions = _definitions;
        if (definitions != null)
        {
            sections.Add("Resources " + (definitions.resources?.Length ?? 0));
            sections.Add("NPCs " + (definitions.npcs?.Length ?? 0));
            sections.Add("Mob spawns " + (definitions.mobSpawns?.Length ?? 0));
        }

        return string.Join(" | ", sections);
    }

    private static void InternalEditorUtilityRepaintAll()
    {
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
}
}
