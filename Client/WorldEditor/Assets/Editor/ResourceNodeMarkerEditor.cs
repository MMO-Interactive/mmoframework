using System;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;

namespace RiseOfHeroes.WorldEditor
{
[CustomEditor(typeof(ResourceNodeMarker))]
public sealed class ResourceNodeMarkerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WorldEditorLiveCatalogCache.EnsureWarm();
        serializedObject.Update();

        using (WorldEditorEditorStyles.Panel("Resource Node", "Live resource definitions drive what can be placed here."))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nodeId"));
            DrawResourceDropdown(serializedObject.FindProperty("resourceId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxCharges"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("respawnSeconds"));
            serializedObject.ApplyModifiedProperties();

            WorldEditorInspectorUi.DrawCatalogToolbar();
        }
    }

    private void OnSceneGUI()
    {
        WorldEditorSceneHandleUtility.DrawTerrainSnappedMoveHandle(((ResourceNodeMarker)target).transform, "Move Resource Node");
    }

    private static void DrawResourceDropdown(SerializedProperty resourceIdProperty)
    {
        var resources = WorldEditorLiveCatalogCache.GetResourceOptions();
        if (resources.Length == 0)
        {
            EditorGUILayout.PropertyField(resourceIdProperty);
            return;
        }

        var labels = resources.Select(entry => entry.label).ToArray();
        var currentIndex = Array.FindIndex(resources, entry => string.Equals(entry.id, resourceIdProperty.stringValue, StringComparison.OrdinalIgnoreCase));
        var selectedIndex = EditorGUILayout.Popup("Resource Type", Math.Max(currentIndex, 0), labels);
        if (selectedIndex >= 0 && selectedIndex < resources.Length)
        {
            resourceIdProperty.stringValue = resources[selectedIndex].id;
        }
    }
}
}
