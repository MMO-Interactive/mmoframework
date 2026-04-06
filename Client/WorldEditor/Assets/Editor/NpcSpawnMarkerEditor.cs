using System;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
[CustomEditor(typeof(NpcSpawnMarker))]
public sealed class NpcSpawnMarkerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WorldEditorLiveCatalogCache.EnsureWarm();
        serializedObject.Update();

        using (WorldEditorEditorStyles.Panel("NPC Spawn", "Template-driven NPC placement with role and service metadata."))
        {
            DrawNpcTemplateDropdown(WorldEditorLiveCatalogCache.GetNpcTemplates(WorldEditorInspectorUi.ResolveCurrentZoneId()));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("npcId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("primaryRole"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("servicesCsv"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("greetingText"));
            serializedObject.ApplyModifiedProperties();

            WorldEditorInspectorUi.DrawCatalogToolbar();
        }
    }

    private void OnSceneGUI()
    {
        WorldEditorSceneHandleUtility.DrawTerrainSnappedMoveHandle(((NpcSpawnMarker)target).transform, "Move NPC");
    }

    private void DrawNpcTemplateDropdown(LiveNpcDefinition[] templates)
    {
        if (templates == null || templates.Length == 0)
        {
            EditorGUILayout.HelpBox("No live NPC templates loaded from the API yet.", MessageType.Info);
            return;
        }

        var npcIdProperty = serializedObject.FindProperty("npcId");
        var displayNameProperty = serializedObject.FindProperty("displayName");
        var primaryRoleProperty = serializedObject.FindProperty("primaryRole");
        var servicesCsvProperty = serializedObject.FindProperty("servicesCsv");
        var greetingTextProperty = serializedObject.FindProperty("greetingText");

        var labels = templates
            .Select(template => (string.IsNullOrWhiteSpace(template.displayName) ? template.npcId : template.displayName) + " | " + template.primaryRole + " | " + template.npcId)
            .ToArray();
        var currentIndex = Array.FindIndex(templates, template => string.Equals(template.npcId, npcIdProperty.stringValue, StringComparison.OrdinalIgnoreCase));
        var selectedIndex = EditorGUILayout.Popup("NPC Template", Math.Max(currentIndex, 0), labels);
        if (selectedIndex >= 0 && selectedIndex < templates.Length && GUILayout.Button("Apply NPC Template"))
        {
            var template = templates[selectedIndex];
            npcIdProperty.stringValue = template.npcId ?? string.Empty;
            displayNameProperty.stringValue = string.IsNullOrWhiteSpace(template.displayName) ? "New NPC" : template.displayName;
            primaryRoleProperty.stringValue = string.IsNullOrWhiteSpace(template.primaryRole) ? "merchant" : template.primaryRole;
            servicesCsvProperty.stringValue = string.Join(",", template.services ?? Array.Empty<string>());
            greetingTextProperty.stringValue = template.greetingText ?? string.Empty;
        }
    }
}
}
