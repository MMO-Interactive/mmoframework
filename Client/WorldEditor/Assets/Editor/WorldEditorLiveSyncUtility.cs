using System;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorLiveSyncUtility
{
    public static void ImportZoneIntoActiveScene(int zoneId, LiveGameplayDefinitionsSnapshot definitions)
    {
        if (zoneId <= 0 || definitions == null)
        {
            return;
        }

        var zone = definitions.zones?.FirstOrDefault(entry => entry.zoneId == zoneId);
        if (zone == null)
        {
            throw new InvalidOperationException("Zone " + zoneId + " was not found in gameplay definitions.");
        }

        EnsureZoneMetadata(zone);
        SyncNodes(zoneId, definitions.nodes ?? Array.Empty<LiveResourceNodeDefinition>());
        SyncNpcs(zoneId, definitions.npcs ?? Array.Empty<LiveNpcDefinition>());
        SyncPlayerSpawns(zoneId, definitions.playerSpawns ?? Array.Empty<LivePlayerSpawnDefinition>());
        SyncMobSpawns(zoneId, definitions.mobSpawns ?? Array.Empty<LiveMobSpawnDefinition>());
    }

    public static LiveGameplayDefinitionsSnapshot BuildMergedSnapshotForScene(int zoneId, LiveGameplayDefinitionsSnapshot current)
    {
        if (current == null)
        {
            current = new LiveGameplayDefinitionsSnapshot();
        }

        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        if (metadata == null)
        {
            throw new InvalidOperationException("The scene has no ZoneSceneMetadata.");
        }

        var nodes = UnityEngine.Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None);
        var npcs = UnityEngine.Object.FindObjectsByType<NpcSpawnMarker>(FindObjectsSortMode.None);
        var playerSpawns = UnityEngine.Object.FindObjectsByType<PlayerSpawnMarker>(FindObjectsSortMode.None);
        var mobSpawns = UnityEngine.Object.FindObjectsByType<MobSpawnMarker>(FindObjectsSortMode.None);

        var nextZones = (current.zones ?? Array.Empty<LiveZoneDefinition>())
            .Where(entry => entry.zoneId != zoneId)
            .Concat(new[]
            {
                new LiveZoneDefinition
                {
                    zoneId = metadata.ZoneId,
                    name = metadata.ZoneName,
                    zoneAssetBundle = metadata.AssetBundleName,
                    minX = metadata.MinBounds.x,
                    maxX = metadata.MaxBounds.x,
                    minZ = metadata.MinBounds.y,
                    maxZ = metadata.MaxBounds.y
                }
            })
            .OrderBy(entry => entry.zoneId)
            .ToArray();

        var nextNodes = (current.nodes ?? Array.Empty<LiveResourceNodeDefinition>())
            .Where(entry => entry.zoneId != zoneId)
            .Concat(nodes.Select(marker => new LiveResourceNodeDefinition
            {
                id = marker.NodeId,
                zoneId = zoneId,
                resourceId = marker.ResourceId,
                positionX = marker.transform.position.x,
                positionY = marker.transform.position.y,
                positionZ = marker.transform.position.z,
                respawnSeconds = Mathf.RoundToInt(marker.RespawnSeconds)
            }))
            .OrderBy(entry => entry.zoneId)
            .ThenBy(entry => entry.id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nextNpcs = (current.npcs ?? Array.Empty<LiveNpcDefinition>())
            .Where(entry => entry.zoneId != zoneId)
            .Concat(npcs.Select(marker => new LiveNpcDefinition
            {
                npcId = marker.NpcId,
                zoneId = zoneId,
                npcTypeId = string.IsNullOrWhiteSpace(marker.PrimaryRole) ? "npc" : marker.PrimaryRole,
                displayName = marker.DisplayName,
                positionX = marker.transform.position.x,
                positionY = marker.transform.position.y,
                positionZ = marker.transform.position.z,
                primaryRole = marker.PrimaryRole,
                services = SplitCsv(marker.ServicesCsv),
                greetingText = marker.GreetingText,
                serviceOptions = Array.Empty<LiveNpcServiceDefinition>()
            }))
            .OrderBy(entry => entry.zoneId)
            .ThenBy(entry => entry.npcId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nextPlayerSpawns = (current.playerSpawns ?? Array.Empty<LivePlayerSpawnDefinition>())
            .Where(entry => entry.zoneId != zoneId)
            .Concat(playerSpawns.Select(marker => new LivePlayerSpawnDefinition
            {
                spawnId = marker.SpawnId,
                zoneId = zoneId,
                positionX = marker.transform.position.x,
                positionY = marker.transform.position.y,
                positionZ = marker.transform.position.z,
                isDefaultSpawn = marker.IsDefaultSpawn,
                facingYaw = marker.FacingYaw,
                spawnTag = marker.SpawnTag
            }))
            .OrderBy(entry => entry.zoneId)
            .ThenByDescending(entry => entry.isDefaultSpawn)
            .ThenBy(entry => entry.spawnTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.spawnId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nextMobSpawns = (current.mobSpawns ?? Array.Empty<LiveMobSpawnDefinition>())
            .Where(entry => entry.zoneId != zoneId)
            .Concat(mobSpawns.Select(marker => new LiveMobSpawnDefinition
            {
                spawnId = marker.SpawnId,
                zoneId = zoneId,
                mobTypeId = marker.MobTypeId,
                positionX = marker.transform.position.x,
                positionY = marker.transform.position.y,
                positionZ = marker.transform.position.z,
                count = marker.Count,
                radius = marker.Radius,
                roamRadius = marker.RoamRadius
            }))
            .OrderBy(entry => entry.zoneId)
            .ThenBy(entry => entry.mobTypeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.spawnId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LiveGameplayDefinitionsSnapshot
        {
            items = current.items ?? Array.Empty<LiveItemDefinition>(),
            skills = current.skills ?? Array.Empty<LiveSkillDefinition>(),
            resources = current.resources ?? Array.Empty<LiveResourceDefinition>(),
            nodes = nextNodes,
            zones = nextZones,
            npcs = nextNpcs,
            playerSpawns = nextPlayerSpawns,
            mobSpawns = nextMobSpawns
        };
    }

    private static void EnsureZoneMetadata(LiveZoneDefinition zone)
    {
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        if (metadata == null)
        {
            var root = new GameObject("Zone Metadata");
            metadata = root.AddComponent<ZoneSceneMetadata>();
        }

        Undo.RecordObject(metadata, "Import Zone Metadata");
        metadata.ZoneId = zone.zoneId;
        metadata.ZoneName = zone.name;
        metadata.AssetBundleName = zone.zoneAssetBundle;
        metadata.MinBounds = new Vector2(zone.minX, zone.minZ);
        metadata.MaxBounds = new Vector2(zone.maxX, zone.maxZ);
        EditorUtility.SetDirty(metadata);
    }

    private static void SyncNodes(int zoneId, LiveResourceNodeDefinition[] incoming)
    {
        var existing = UnityEngine.Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None)
            .ToDictionary(entry => entry.NodeId, StringComparer.OrdinalIgnoreCase);

        foreach (var node in incoming.Where(entry => entry.zoneId == zoneId))
        {
            if (!existing.TryGetValue(node.id, out var marker))
            {
                var go = new GameObject("Node " + node.id);
                marker = go.AddComponent<ResourceNodeMarker>();
            }

            Undo.RecordObject(marker.gameObject.transform, "Import Resource Node");
            marker.gameObject.name = "Node " + node.id;
            marker.transform.position = new Vector3(node.positionX, node.positionY, node.positionZ);
            var so = new SerializedObject(marker);
            so.FindProperty("nodeId").stringValue = node.id ?? string.Empty;
            so.FindProperty("resourceId").stringValue = node.resourceId ?? "wood";
            so.FindProperty("respawnSeconds").floatValue = Mathf.Max(1f, node.respawnSeconds);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(marker);
        }
    }

    private static void SyncNpcs(int zoneId, LiveNpcDefinition[] incoming)
    {
        var existing = UnityEngine.Object.FindObjectsByType<NpcSpawnMarker>(FindObjectsSortMode.None)
            .ToDictionary(entry => entry.NpcId, StringComparer.OrdinalIgnoreCase);

        foreach (var npc in incoming.Where(entry => entry.zoneId == zoneId))
        {
            if (!existing.TryGetValue(npc.npcId, out var marker))
            {
                var go = new GameObject("NPC " + npc.displayName);
                marker = go.AddComponent<NpcSpawnMarker>();
            }

            Undo.RecordObject(marker.gameObject.transform, "Import NPC");
            marker.gameObject.name = "NPC " + (string.IsNullOrWhiteSpace(npc.displayName) ? npc.npcId : npc.displayName);
            marker.transform.position = new Vector3(npc.positionX, npc.positionY, npc.positionZ);
            var so = new SerializedObject(marker);
            so.FindProperty("npcId").stringValue = npc.npcId ?? string.Empty;
            so.FindProperty("displayName").stringValue = npc.displayName ?? "NPC";
            so.FindProperty("primaryRole").stringValue = npc.primaryRole ?? "npc";
            so.FindProperty("servicesCsv").stringValue = string.Join(",", npc.services ?? Array.Empty<string>());
            so.FindProperty("greetingText").stringValue = npc.greetingText ?? string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(marker);
        }
    }

    private static void SyncPlayerSpawns(int zoneId, LivePlayerSpawnDefinition[] incoming)
    {
        var existing = UnityEngine.Object.FindObjectsByType<PlayerSpawnMarker>(FindObjectsSortMode.None)
            .ToDictionary(entry => entry.SpawnId, StringComparer.OrdinalIgnoreCase);

        foreach (var spawn in incoming.Where(entry => entry.zoneId == zoneId))
        {
            if (!existing.TryGetValue(spawn.spawnId, out var marker))
            {
                var go = new GameObject("Player Spawn " + spawn.spawnId);
                marker = go.AddComponent<PlayerSpawnMarker>();
            }

            Undo.RecordObject(marker.gameObject.transform, "Import Player Spawn");
            marker.gameObject.name = "Player Spawn " + (string.IsNullOrWhiteSpace(spawn.spawnTag) ? spawn.spawnId : spawn.spawnTag);
            marker.transform.position = new Vector3(spawn.positionX, spawn.positionY, spawn.positionZ);
            var so = new SerializedObject(marker);
            so.FindProperty("spawnId").stringValue = spawn.spawnId ?? string.Empty;
            so.FindProperty("isDefaultSpawn").boolValue = spawn.isDefaultSpawn;
            so.FindProperty("facingYaw").floatValue = spawn.facingYaw;
            so.FindProperty("spawnTag").stringValue = string.IsNullOrWhiteSpace(spawn.spawnTag) ? "starter" : spawn.spawnTag;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(marker);
        }
    }

    private static void SyncMobSpawns(int zoneId, LiveMobSpawnDefinition[] incoming)
    {
        var existing = UnityEngine.Object.FindObjectsByType<MobSpawnMarker>(FindObjectsSortMode.None)
            .ToDictionary(entry => entry.SpawnId, StringComparer.OrdinalIgnoreCase);

        foreach (var spawn in incoming.Where(entry => entry.zoneId == zoneId))
        {
            if (!existing.TryGetValue(spawn.spawnId, out var marker))
            {
                var go = new GameObject("Mob Spawn " + spawn.spawnId);
                marker = go.AddComponent<MobSpawnMarker>();
            }

            Undo.RecordObject(marker.gameObject.transform, "Import Mob Spawn");
            marker.gameObject.name = "Mob Spawn " + (string.IsNullOrWhiteSpace(spawn.mobTypeId) ? spawn.spawnId : spawn.mobTypeId);
            marker.transform.position = new Vector3(spawn.positionX, spawn.positionY, spawn.positionZ);
            var so = new SerializedObject(marker);
            so.FindProperty("spawnId").stringValue = spawn.spawnId ?? string.Empty;
            so.FindProperty("mobTypeId").stringValue = string.IsNullOrWhiteSpace(spawn.mobTypeId) ? "wolf" : spawn.mobTypeId;
            so.FindProperty("count").intValue = Math.Max(1, spawn.count);
            so.FindProperty("radius").floatValue = Mathf.Max(0.5f, spawn.radius);
            so.FindProperty("roamRadius").floatValue = Mathf.Max(Mathf.Max(0.5f, spawn.radius), spawn.roamRadius);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(marker);
        }
    }

    private static string[] SplitCsv(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(',')
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
}
