using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
[CustomEditor(typeof(PlayerSpawnMarker))]
public sealed class PlayerSpawnMarkerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WorldEditorLiveCatalogCache.EnsureWarm();
        serializedObject.Update();

        using (WorldEditorEditorStyles.Panel("Player Spawn", "Configure login and respawn placement for characters."))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isDefaultSpawn"));
            EditorGUILayout.Slider(serializedObject.FindProperty("facingYaw"), 0f, 360f);
            WorldEditorInspectorUi.DrawStringDropdown("Spawn Tag", serializedObject.FindProperty("spawnTag"), WorldEditorLiveCatalogCache.GetSpawnTags());
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Snap To Terrain"))
            {
                WorldEditorTerrainUtility.SnapAllMarkersToTerrain();
            }

            WorldEditorInspectorUi.DrawCatalogToolbar();
        }
    }

    private void OnSceneGUI()
    {
        WorldEditorSceneHandleUtility.DrawTerrainSnappedMoveHandle(((PlayerSpawnMarker)target).transform, "Move Player Spawn");
    }
}
}
