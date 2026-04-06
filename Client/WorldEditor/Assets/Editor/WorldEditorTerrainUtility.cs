using System;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorTerrainUtility
{
    [MenuItem("Rise of Heroes/MMO Editor/Create Standard Zone Terrain", priority = 20)]
    public static void CreateStandardZoneTerrainMenu()
    {
        CreateOrReplaceStandardZoneTerrain();
    }

    [MenuItem("Rise of Heroes/MMO Editor/Align Zone Metadata To Terrain", priority = 21)]
    public static void AlignZoneMetadataToTerrainMenu()
    {
        AlignZoneMetadataToTerrain();
    }

    [MenuItem("Rise of Heroes/MMO Editor/Snap All Markers To Terrain", priority = 22)]
    public static void SnapAllMarkersToTerrainMenu()
    {
        SnapAllMarkersToTerrain();
    }

    [MenuItem("Rise of Heroes/MMO Editor/Create Starter Zone Layout", priority = 23)]
    public static void CreateStarterZoneLayoutMenu()
    {
        CreateStarterZoneLayout();
    }

    public static Terrain CreateOrReplaceStandardZoneTerrain()
    {
        var existingTerrain = Terrain.activeTerrain;
        if (existingTerrain != null)
        {
            ConfigureTerrain(existingTerrain);
            EnsureZoneMetadataAligned(existingTerrain.transform.position);
            Selection.activeObject = existingTerrain.gameObject;
            EditorSceneManager.MarkSceneDirty(existingTerrain.gameObject.scene);
            return existingTerrain;
        }

        var data = new TerrainData
        {
            heightmapResolution = 513,
            size = new Vector3(
                ZoneSceneMetadata.StandardTerrainSizeMeters,
                600f,
                ZoneSceneMetadata.StandardTerrainSizeMeters)
        };
        data.SetDetailResolution(1024, 16);
        data.baseMapResolution = 1024;
        data.alphamapResolution = 1024;

        var terrainGameObject = Terrain.CreateTerrainGameObject(data);
        terrainGameObject.name = "Zone Terrain";
        Undo.RegisterCreatedObjectUndo(terrainGameObject, "Create Standard Zone Terrain");

        var terrain = terrainGameObject.GetComponent<Terrain>();
        ConfigureTerrain(terrain);
        terrain.transform.position = Vector3.zero;
        EnsureZoneMetadataAligned(terrain.transform.position);
        Selection.activeObject = terrainGameObject;
        EditorSceneManager.MarkSceneDirty(terrainGameObject.scene);
        return terrain;
    }

    public static void AlignZoneMetadataToTerrain()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            throw new InvalidOperationException("No active Terrain exists in the scene.");
        }

        ConfigureTerrain(terrain);
        EnsureZoneMetadataAligned(terrain.transform.position);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
    }

    public static void SnapAllMarkersToTerrain()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            throw new InvalidOperationException("No active Terrain exists in the scene.");
        }

        var scene = terrain.gameObject.scene;
        SnapObjects(UnityEngine.Object.FindObjectsByType<PlayerSpawnMarker>(FindObjectsSortMode.None).Select(marker => marker.transform).ToArray(), "Snap Player Spawns To Terrain");
        SnapObjects(UnityEngine.Object.FindObjectsByType<NpcSpawnMarker>(FindObjectsSortMode.None).Select(marker => marker.transform).ToArray(), "Snap NPCs To Terrain");
        SnapObjects(UnityEngine.Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None).Select(marker => marker.transform).ToArray(), "Snap Resource Nodes To Terrain");
        SnapObjects(UnityEngine.Object.FindObjectsByType<MobSpawnMarker>(FindObjectsSortMode.None).Select(marker => marker.transform).ToArray(), "Snap Mob Spawns To Terrain");
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void CreateStarterZoneLayout()
    {
        var terrain = CreateOrReplaceStandardZoneTerrain();
        EnsureStarterMetadata();

        CreateOrMoveMarker("Player Spawn Starter", new Vector3(140f, 0f, 140f), go => go.AddComponent<PlayerSpawnMarker>());
        var merchant = CreateOrMoveMarker("NPC Quartermaster Rowan", new Vector3(220f, 0f, 190f), go => go.AddComponent<NpcSpawnMarker>());
        ApplyNpcDefaults(merchant, "merchant-1", "Quartermaster Rowan", "merchant", "shop,crafting", "Supplies for the road, tools for the trade, and a fair barter if your pack is worth opening.");
        var questGiver = CreateOrMoveMarker("NPC Warden Elira", new Vector3(248f, 0f, 214f), go => go.AddComponent<NpcSpawnMarker>());
        ApplyNpcDefaults(questGiver, "questgiver-1", "Warden Elira", "quest", "quests", "Every frontier needs hands willing to work. If you want purpose, I have tasks that matter.");
        var trainer = CreateOrMoveMarker("NPC Master Toren", new Vector3(278f, 0f, 186f), go => go.AddComponent<NpcSpawnMarker>());
        ApplyNpcDefaults(trainer, "trainer-1", "Master Toren", "trainer", "training,progression", "Skill is earned, not granted. Show me what you've practiced, and I'll show you where to sharpen it next.");

        var tree = CreateOrMoveMarker("Node tree-1", new Vector3(320f, 0f, 320f), go => go.AddComponent<ResourceNodeMarker>());
        ApplyResourceDefaults(tree, "tree-1", "tree", 6, 8f);
        var ore = CreateOrMoveMarker("Node ore-1", new Vector3(390f, 0f, 410f), go => go.AddComponent<ResourceNodeMarker>());
        ApplyResourceDefaults(ore, "ore-1", "ore_vein", 5, 8f);

        var wolf = CreateOrMoveMarker("Mob Spawn wolf", new Vector3(760f, 0f, 720f), go => go.AddComponent<MobSpawnMarker>());
        ApplyMobDefaults(wolf, "mob-1-pack-a", "wolf", 12, 26f, 40f);
        var boar = CreateOrMoveMarker("Mob Spawn boar", new Vector3(1180f, 0f, 980f), go => go.AddComponent<MobSpawnMarker>());
        ApplyMobDefaults(boar, "mob-1-pack-b", "boar", 8, 22f, 34f);

        SnapAllMarkersToTerrain();
        Selection.activeObject = terrain.gameObject;
    }

    public static Vector3 SnapPositionToTerrain(Vector3 worldPosition)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
        {
            return new Vector3(worldPosition.x, 0f, worldPosition.z);
        }

        var terrainPosition = terrain.transform.position;
        var localX = worldPosition.x - terrainPosition.x;
        var localZ = worldPosition.z - terrainPosition.z;
        var size = terrain.terrainData.size;
        localX = Mathf.Clamp(localX, 0f, size.x);
        localZ = Mathf.Clamp(localZ, 0f, size.z);
        var height = terrain.SampleHeight(new Vector3(terrainPosition.x + localX, 0f, terrainPosition.z + localZ)) + terrainPosition.y;
        return new Vector3(terrainPosition.x + localX, height, terrainPosition.z + localZ);
    }

    private static void ConfigureTerrain(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            throw new InvalidOperationException("Terrain is missing TerrainData.");
        }

        Undo.RecordObject(terrain.terrainData, "Configure Standard Zone Terrain");
        terrain.terrainData.size = new Vector3(
            ZoneSceneMetadata.StandardTerrainSizeMeters,
            terrain.terrainData.size.y <= 0f ? 600f : terrain.terrainData.size.y,
            ZoneSceneMetadata.StandardTerrainSizeMeters);
        terrain.drawInstanced = true;
        EditorUtility.SetDirty(terrain.terrainData);
        EditorUtility.SetDirty(terrain);
    }

    private static void SnapObjects(Transform[] transforms, string undoLabel)
    {
        for (var i = 0; i < transforms.Length; i++)
        {
            var targetTransform = transforms[i];
            if (targetTransform == null)
            {
                continue;
            }

            Undo.RecordObject(targetTransform, undoLabel);
            targetTransform.position = SnapPositionToTerrain(targetTransform.position);
        }
    }

    private static void EnsureZoneMetadataAligned(Vector3 terrainOrigin)
    {
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        if (metadata == null)
        {
            var root = new GameObject("Zone Metadata");
            Undo.RegisterCreatedObjectUndo(root, "Create Zone Metadata");
            metadata = root.AddComponent<ZoneSceneMetadata>();
        }

        Undo.RecordObject(metadata, "Align Zone Metadata To Terrain");
        metadata.ApplyStandardBounds(terrainOrigin);
        EditorUtility.SetDirty(metadata);
    }

    private static void EnsureStarterMetadata()
    {
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        if (metadata == null)
        {
            var root = new GameObject("Zone Metadata");
            Undo.RegisterCreatedObjectUndo(root, "Create Zone Metadata");
            metadata = root.AddComponent<ZoneSceneMetadata>();
        }

        Undo.RecordObject(metadata, "Configure Starter Zone Metadata");
        metadata.ZoneName = string.IsNullOrWhiteSpace(metadata.ZoneName) || metadata.ZoneName == "New Zone" ? "StarterZone" : metadata.ZoneName;
        metadata.EnvironmentTag = string.IsNullOrWhiteSpace(metadata.EnvironmentTag) ? "temperate" : metadata.EnvironmentTag;
        metadata.ApplyStandardBounds(Vector3.zero);
        EditorUtility.SetDirty(metadata);
    }

    private static T CreateOrMoveMarker<T>(string objectName, Vector3 worldPosition, Func<GameObject, T> factory) where T : Component
    {
        var existing = UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault(entry => entry.gameObject.name == objectName);
        if (existing != null)
        {
            Undo.RecordObject(existing.transform, "Move " + objectName);
            existing.transform.position = SnapPositionToTerrain(worldPosition);
            return existing;
        }

        var markerObject = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(markerObject, "Create " + objectName);
        markerObject.transform.position = SnapPositionToTerrain(worldPosition);
        return factory(markerObject);
    }

    private static void ApplyNpcDefaults(NpcSpawnMarker marker, string npcId, string displayName, string role, string services, string greeting)
    {
        var so = new SerializedObject(marker);
        so.FindProperty("npcId").stringValue = npcId;
        so.FindProperty("displayName").stringValue = displayName;
        so.FindProperty("primaryRole").stringValue = role;
        so.FindProperty("servicesCsv").stringValue = services;
        so.FindProperty("greetingText").stringValue = greeting;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(marker);
    }

    private static void ApplyResourceDefaults(ResourceNodeMarker marker, string nodeId, string resourceId, int maxCharges, float respawnSeconds)
    {
        var so = new SerializedObject(marker);
        so.FindProperty("nodeId").stringValue = nodeId;
        so.FindProperty("resourceId").stringValue = resourceId;
        so.FindProperty("maxCharges").intValue = maxCharges;
        so.FindProperty("respawnSeconds").floatValue = respawnSeconds;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(marker);
    }

    private static void ApplyMobDefaults(MobSpawnMarker marker, string spawnId, string mobTypeId, int count, float radius, float roamRadius)
    {
        var so = new SerializedObject(marker);
        so.FindProperty("spawnId").stringValue = spawnId;
        so.FindProperty("mobTypeId").stringValue = mobTypeId;
        so.FindProperty("count").intValue = count;
        so.FindProperty("radius").floatValue = radius;
        so.FindProperty("roamRadius").floatValue = roamRadius;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(marker);
    }
}
}
