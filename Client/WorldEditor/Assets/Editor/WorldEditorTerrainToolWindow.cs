using System;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public sealed class WorldEditorTerrainToolWindow : EditorWindow
{
    [MenuItem("Rise of Heroes/MMO Editor/Tools/Terrain", priority = 7)]
    public static void Open()
    {
        var window = GetWindow<WorldEditorTerrainToolWindow>("Terrain Tool");
        window.minSize = new Vector2(520f, 380f);
    }

    private void OnGUI()
    {
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        var terrain = Terrain.activeTerrain;

        using (WorldEditorEditorStyles.Panel("Terrain Status", "Standard zones are authored as one 2000m x 2000m Unity Terrain tile."))
        {
            if (terrain == null)
            {
                EditorGUILayout.HelpBox("No Terrain is present in the scene.", MessageType.Warning);
            }
            else
            {
                var size = terrain.terrainData != null ? terrain.terrainData.size : Vector3.zero;
                var isStandard =
                    Mathf.Abs(size.x - ZoneSceneMetadata.StandardTerrainSizeMeters) < 0.01f &&
                    Mathf.Abs(size.z - ZoneSceneMetadata.StandardTerrainSizeMeters) < 0.01f;
                EditorGUILayout.LabelField("Active Terrain", terrain.name);
                EditorGUILayout.LabelField("Terrain Size", size.x.ToString("F0") + " x " + size.z.ToString("F0") + " meters");
                EditorGUILayout.LabelField("Terrain Origin", terrain.transform.position.x.ToString("F1") + ", " + terrain.transform.position.z.ToString("F1"));
                EditorGUILayout.HelpBox(
                    isStandard ? "Terrain matches the standard zone tile." : "Terrain does not match the standard 2000m x 2000m tile.",
                    isStandard ? MessageType.None : MessageType.Warning);
            }
        }

        using (WorldEditorEditorStyles.Panel("Terrain Actions"))
        {
            if (GUILayout.Button("Create / Normalize Standard Terrain"))
            {
                ExecuteTerrainAction(WorldEditorTerrainUtility.CreateOrReplaceStandardZoneTerrain);
            }

            if (GUILayout.Button("Align Metadata To Terrain"))
            {
                ExecuteTerrainAction(() =>
                {
                    WorldEditorTerrainUtility.AlignZoneMetadataToTerrain();
                    return null;
                });
            }

            if (GUILayout.Button("Snap All Markers To Terrain"))
            {
                ExecuteTerrainAction(() =>
                {
                    WorldEditorTerrainUtility.SnapAllMarkersToTerrain();
                    return null;
                });
            }

            if (GUILayout.Button("Create Starter Zone Layout"))
            {
                ExecuteTerrainAction(() =>
                {
                    WorldEditorTerrainUtility.CreateStarterZoneLayout();
                    return null;
                });
            }

            if (metadata != null && GUILayout.Button("Snap Scene View To Terrain Center"))
            {
                var center = metadata.TerrainOrigin + new Vector3(
                    ZoneSceneMetadata.StandardTerrainSizeMeters * 0.5f,
                    0f,
                    ZoneSceneMetadata.StandardTerrainSizeMeters * 0.5f);
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.pivot = center;
                    SceneView.lastActiveSceneView.size = ZoneSceneMetadata.StandardTerrainSizeMeters * 0.75f;
                    SceneView.lastActiveSceneView.Repaint();
                }
            }
        }
    }

    private static void ExecuteTerrainAction(Func<UnityEngine.Object> action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
}
