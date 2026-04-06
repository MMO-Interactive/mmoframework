using System;
using System.IO;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorSceneExportUtility
{
    [MenuItem("Rise of Heroes/MMO Editor/Export Active Scene Authoring JSON", priority = 10)]
    public static void ExportActiveSceneAuthoringJson()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(scene.path))
        {
            EditorUtility.DisplayDialog("Export Scene Authoring", "Open and save a scene first.", "OK");
            return;
        }

        var outputDirectory = Path.Combine(ResolveWorkspaceRoot(), "Builds", "WorldEditor", "Authoring");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, scene.name + ".authoring.json");
        ExportActiveSceneAuthoringJson(outputPath);
        EditorUtility.RevealInFinder(outputPath);
        EditorUtility.DisplayDialog("Export Scene Authoring", "Wrote authoring JSON to:\n" + outputPath, "OK");
    }

    public static void ExportActiveSceneAuthoringJson(string outputPath)
    {
        var export = BuildExport();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);
        File.WriteAllText(outputPath, JsonUtility.ToJson(export, true));
        AssetDatabase.Refresh();
    }

    public static WorldSceneExport BuildExport()
    {
        var scene = SceneManager.GetActiveScene();
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        var playerSpawns = UnityEngine.Object.FindObjectsByType<PlayerSpawnMarker>(FindObjectsSortMode.None);
        var npcs = UnityEngine.Object.FindObjectsByType<NpcSpawnMarker>(FindObjectsSortMode.None);
        var resources = UnityEngine.Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None);
        var mobs = UnityEngine.Object.FindObjectsByType<MobSpawnMarker>(FindObjectsSortMode.None);

        return new WorldSceneExport
        {
            sceneName = scene.name,
            scenePath = scene.path ?? string.Empty,
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            zone = metadata == null
                ? new ZoneExportData()
                : new ZoneExportData
                {
                    zoneId = metadata.ZoneId,
                    zoneName = metadata.ZoneName,
                    assetBundleName = metadata.AssetBundleName,
                    minX = metadata.MinBounds.x,
                    minZ = metadata.MinBounds.y,
                    maxX = metadata.MaxBounds.x,
                    maxZ = metadata.MaxBounds.y,
                    environmentTag = metadata.EnvironmentTag,
                    notes = metadata.Notes
                },
            playerSpawns = playerSpawns.Select(marker => new PlayerSpawnExportData
            {
                spawnId = marker.SpawnId,
                isDefaultSpawn = marker.IsDefaultSpawn,
                spawnTag = marker.SpawnTag,
                position = ToVector3(marker.transform.position),
                facingYaw = marker.FacingYaw
            }).ToArray(),
            npcs = npcs.Select(marker => new NpcSpawnExportData
            {
                npcId = marker.NpcId,
                displayName = marker.DisplayName,
                primaryRole = marker.PrimaryRole,
                servicesCsv = marker.ServicesCsv,
                greetingText = marker.GreetingText,
                position = ToVector3(marker.transform.position)
            }).ToArray(),
            resourceNodes = resources.Select(marker => new ResourceNodeExportData
            {
                nodeId = marker.NodeId,
                resourceId = marker.ResourceId,
                maxCharges = marker.MaxCharges,
                respawnSeconds = marker.RespawnSeconds,
                position = ToVector3(marker.transform.position)
            }).ToArray(),
            mobSpawns = mobs.Select(marker => new MobSpawnExportData
            {
                spawnId = marker.SpawnId,
                mobTypeId = marker.MobTypeId,
                count = marker.Count > 0 ? marker.Count : 1,
                radius = marker.Radius,
                roamRadius = marker.RoamRadius,
                position = ToVector3(marker.transform.position)
            }).ToArray()
        };
    }

    public static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
        while (current != null)
        {
            var hasServer = Directory.Exists(Path.Combine(current.FullName, "Server"));
            var hasClient = Directory.Exists(Path.Combine(current.FullName, "Client"));
            var hasShared = Directory.Exists(Path.Combine(current.FullName, "Shared"));
            if (hasServer && hasClient && hasShared)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static SerializableVector3 ToVector3(Vector3 value)
        => new SerializableVector3 { x = value.x, y = value.y, z = value.z };

    [Serializable]
    public sealed class WorldSceneExport
    {
        public string sceneName;
        public string scenePath;
        public string generatedAtUtc;
        public ZoneExportData zone;
        public PlayerSpawnExportData[] playerSpawns = Array.Empty<PlayerSpawnExportData>();
        public NpcSpawnExportData[] npcs = Array.Empty<NpcSpawnExportData>();
        public ResourceNodeExportData[] resourceNodes = Array.Empty<ResourceNodeExportData>();
        public MobSpawnExportData[] mobSpawns = Array.Empty<MobSpawnExportData>();
    }

    [Serializable]
    public sealed class ZoneExportData
    {
        public int zoneId = 1;
        public string zoneName = "New Zone";
        public string assetBundleName = string.Empty;
        public float minX;
        public float minZ;
        public float maxX = ZoneSceneMetadata.StandardTerrainSizeMeters;
        public float maxZ = ZoneSceneMetadata.StandardTerrainSizeMeters;
        public string environmentTag = "temperate";
        public string notes = string.Empty;
    }

    [Serializable]
    public sealed class PlayerSpawnExportData
    {
        public string spawnId;
        public bool isDefaultSpawn;
        public string spawnTag;
        public SerializableVector3 position;
        public float facingYaw;
    }

    [Serializable]
    public sealed class NpcSpawnExportData
    {
        public string npcId;
        public string displayName;
        public string primaryRole;
        public string servicesCsv;
        public string greetingText;
        public SerializableVector3 position;
    }

    [Serializable]
    public sealed class ResourceNodeExportData
    {
        public string nodeId;
        public string resourceId;
        public int maxCharges;
        public float respawnSeconds;
        public SerializableVector3 position;
    }

    [Serializable]
    public sealed class MobSpawnExportData
    {
        public string spawnId;
        public string mobTypeId;
        public int count;
        public float radius;
        public float roamRadius;
        public SerializableVector3 position;
    }

    [Serializable]
    public sealed class SerializableVector3
    {
        public float x;
        public float y;
        public float z;
    }
}
}
