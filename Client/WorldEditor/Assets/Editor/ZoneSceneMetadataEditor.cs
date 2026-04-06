using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
[CustomEditor(typeof(ZoneSceneMetadata))]
public sealed class ZoneSceneMetadataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (WorldEditorEditorStyles.Panel("Zone Metadata", "Scene-level identity, footprint, and bundle assignment."))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("zoneId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("zoneName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("assetBundleName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("minBounds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxBounds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("environmentTag"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("notes"));
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Align To Standard Terrain"))
            {
                WorldEditorTerrainUtility.AlignZoneMetadataToTerrain();
            }
        }
    }

    private void OnSceneGUI()
    {
        WorldEditorSceneHandleUtility.DrawZoneBoundsHandle((ZoneSceneMetadata)target);
    }
}
}
