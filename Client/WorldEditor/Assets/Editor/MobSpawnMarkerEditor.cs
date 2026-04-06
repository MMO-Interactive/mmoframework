using System;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
[CustomEditor(typeof(MobSpawnMarker))]
public sealed class MobSpawnMarkerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WorldEditorLiveCatalogCache.EnsureWarm();
        serializedObject.Update();

        using (WorldEditorEditorStyles.Panel("Mob Spawn", "Live mob definitions populate the spawn archetype list."))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnId"));
            DrawMobTypeDropdown(serializedObject.FindProperty("mobTypeId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("count"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("radius"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roamRadius"));
            serializedObject.ApplyModifiedProperties();

            WorldEditorInspectorUi.DrawCatalogToolbar();
        }
    }

    private void OnSceneGUI()
    {
        var marker = (MobSpawnMarker)target;
        WorldEditorSceneHandleUtility.DrawTerrainSnappedMoveHandle(marker.transform, "Move Mob Spawn");

        var serializedMarker = new SerializedObject(marker);
        var radiusProperty = serializedMarker.FindProperty("radius");
        var roamRadiusProperty = serializedMarker.FindProperty("roamRadius");

        EditorGUI.BeginChangeCheck();
        var nextRadius = Handles.RadiusHandle(Quaternion.identity, marker.transform.position, radiusProperty.floatValue);
        if (EditorGUI.EndChangeCheck())
        {
            serializedMarker.Update();
            radiusProperty.floatValue = Mathf.Max(0.5f, nextRadius);
            if (roamRadiusProperty.floatValue < radiusProperty.floatValue)
            {
                roamRadiusProperty.floatValue = radiusProperty.floatValue;
            }
            serializedMarker.ApplyModifiedProperties();
        }

        EditorGUI.BeginChangeCheck();
        var nextRoamRadius = Handles.RadiusHandle(Quaternion.identity, marker.transform.position, roamRadiusProperty.floatValue);
        if (EditorGUI.EndChangeCheck())
        {
            serializedMarker.Update();
            roamRadiusProperty.floatValue = Mathf.Max(radiusProperty.floatValue, nextRoamRadius);
            serializedMarker.ApplyModifiedProperties();
        }
    }

    private static void DrawMobTypeDropdown(SerializedProperty mobTypeIdProperty)
    {
        var mobTypes = WorldEditorLiveCatalogCache.GetMobTypeOptions();
        if (mobTypes.Length == 0)
        {
            EditorGUILayout.PropertyField(mobTypeIdProperty);
            return;
        }

        var currentIndex = Array.FindIndex(mobTypes, entry => string.Equals(entry, mobTypeIdProperty.stringValue, StringComparison.OrdinalIgnoreCase));
        var selectedIndex = EditorGUILayout.Popup("Mob Type", Math.Max(currentIndex, 0), mobTypes);
        if (selectedIndex >= 0 && selectedIndex < mobTypes.Length)
        {
            mobTypeIdProperty.stringValue = mobTypes[selectedIndex];
        }
    }
}
}
