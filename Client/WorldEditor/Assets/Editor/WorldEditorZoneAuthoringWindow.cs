using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public sealed class WorldEditorZoneAuthoringWindow : EditorWindow
{
    private Vector2 _scroll;

    [MenuItem("Rise of Heroes/MMO Editor/Tools/Zone Authoring", priority = 6)]
    public static void Open()
    {
        var window = GetWindow<WorldEditorZoneAuthoringWindow>("Zone Authoring");
        window.minSize = new Vector2(560f, 640f);
    }

    private void OnGUI()
    {
        var validation = WorldEditorWorkspace.ValidateActiveScene();
        if (!validation.Scene.IsValid() || string.IsNullOrWhiteSpace(validation.Scene.path))
        {
            EditorGUILayout.HelpBox(validation.Message, MessageType.Info);
            return;
        }

        if (validation.Metadata == null)
        {
            EditorGUILayout.HelpBox(validation.Message, MessageType.Warning);
            if (GUILayout.Button("Create Zone Metadata"))
            {
                WorldEditorWorkspace.CreateZoneMetadata();
            }

            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawZoneSection(validation.Metadata);
        DrawCreateToolbar();
        DrawCollectionSections();
        EditorGUILayout.EndScrollView();
    }

    private static void DrawZoneSection(ZoneSceneMetadata metadata)
    {
        using (WorldEditorEditorStyles.Panel("Zone Metadata", "Author the zone identity and authored footprint."))
        {
            EditorGUI.BeginChangeCheck();
            var zoneId = EditorGUILayout.IntField("Zone Id", metadata.ZoneId);
            var zoneName = EditorGUILayout.TextField("Zone Name", metadata.ZoneName);
            var bundleName = EditorGUILayout.TextField("Asset Bundle", metadata.AssetBundleName);
            var minBounds = EditorGUILayout.Vector2Field("Min Bounds", metadata.MinBounds);
            var maxBounds = EditorGUILayout.Vector2Field("Max Bounds", metadata.MaxBounds);
            var environment = EditorGUILayout.TextField("Environment", metadata.EnvironmentTag);
            var notes = EditorGUILayout.TextField("Notes", metadata.Notes);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(metadata, "Edit Zone Metadata");
                metadata.ZoneId = zoneId;
                metadata.ZoneName = zoneName;
                metadata.AssetBundleName = bundleName;
                metadata.MinBounds = minBounds;
                metadata.MaxBounds = maxBounds;
                metadata.EnvironmentTag = environment;
                metadata.Notes = notes;
                EditorUtility.SetDirty(metadata);
            }

            var size = metadata.Size;
            var matchesStandardTerrain =
                Mathf.Abs(size.x - ZoneSceneMetadata.StandardTerrainSizeMeters) < 0.01f &&
                Mathf.Abs(size.y - ZoneSceneMetadata.StandardTerrainSizeMeters) < 0.01f;
            EditorGUILayout.HelpBox(
                "Zone footprint: " + size.x.ToString("F0") + "m x " + size.y.ToString("F0") +
                (matchesStandardTerrain ? " | standard terrain size" : " | expected standard is 2000m x 2000m"),
                matchesStandardTerrain ? MessageType.None : MessageType.Warning);
        }
    }

    private static void DrawCreateToolbar()
    {
        using (WorldEditorEditorStyles.Panel("Placement Tools", "Create scene authoring markers for the active zone."))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Player Spawn"))
                {
                    WorldEditorWorkspace.CreateMarker<PlayerSpawnMarker>("Player Spawn");
                }

                if (GUILayout.Button("NPC"))
                {
                    WorldEditorWorkspace.CreateMarker<NpcSpawnMarker>("NPC");
                }

                if (GUILayout.Button("Resource Node"))
                {
                    WorldEditorWorkspace.CreateMarker<ResourceNodeMarker>("Resource Node");
                }

                if (GUILayout.Button("Mob Spawn"))
                {
                    WorldEditorWorkspace.CreateMarker<MobSpawnMarker>("Mob Spawn");
                }
            }
        }
    }

    private static void DrawCollectionSections()
    {
        using (WorldEditorEditorStyles.Panel("Authoring Collections"))
        {
            WorldEditorWorkspace.DrawCollectionSection("Player Spawns", FindObjectsByType<PlayerSpawnMarker>(FindObjectsSortMode.None), DrawPlayerSpawnRow);
            WorldEditorWorkspace.DrawCollectionSection("NPCs", FindObjectsByType<NpcSpawnMarker>(FindObjectsSortMode.None), DrawNpcRow);
            WorldEditorWorkspace.DrawCollectionSection("Resource Nodes", FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None), DrawResourceRow);
            WorldEditorWorkspace.DrawCollectionSection("Mob Spawns", FindObjectsByType<MobSpawnMarker>(FindObjectsSortMode.None), DrawMobRow);
        }
    }

    private static void DrawPlayerSpawnRow(PlayerSpawnMarker marker)
    {
        WorldEditorWorkspace.DrawGenericRow(marker.gameObject.name, marker.SpawnId, marker.transform.position, marker.gameObject);
    }

    private static void DrawNpcRow(NpcSpawnMarker marker)
    {
        WorldEditorWorkspace.DrawGenericRow(marker.DisplayName, marker.NpcId + " | " + marker.PrimaryRole, marker.transform.position, marker.gameObject);
    }

    private static void DrawResourceRow(ResourceNodeMarker marker)
    {
        WorldEditorWorkspace.DrawGenericRow(marker.ResourceId, marker.NodeId, marker.transform.position, marker.gameObject);
    }

    private static void DrawMobRow(MobSpawnMarker marker)
    {
        WorldEditorWorkspace.DrawGenericRow(marker.MobTypeId + " x" + marker.Count, marker.SpawnId, marker.transform.position, marker.gameObject);
    }
}
}
