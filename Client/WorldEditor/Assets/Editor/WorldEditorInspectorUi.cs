using System;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
internal static class WorldEditorInspectorUi
{
    public static void DrawCatalogToolbar()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(WorldEditorLiveCatalogCache.BuildStatusLabel(), MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !WorldEditorLiveCatalogCache.IsRefreshing;
            if (GUILayout.Button("Refresh Live Catalog"))
            {
                _ = WorldEditorLiveCatalogCache.RefreshAsync();
            }
            GUI.enabled = true;
        }
    }

    public static void DrawStringDropdown(string label, SerializedProperty property, string[] options)
    {
        if (options == null || options.Length == 0)
        {
            EditorGUILayout.PropertyField(property);
            return;
        }

        var labels = options
            .Concat(string.IsNullOrWhiteSpace(property.stringValue) || options.Any(entry => string.Equals(entry, property.stringValue, StringComparison.OrdinalIgnoreCase))
                ? Array.Empty<string>()
                : new[] { property.stringValue })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentIndex = Array.FindIndex(labels, entry => string.Equals(entry, property.stringValue, StringComparison.OrdinalIgnoreCase));
        var selectedIndex = EditorGUILayout.Popup(label, Math.Max(currentIndex, 0), labels);
        if (selectedIndex >= 0 && selectedIndex < labels.Length)
        {
            property.stringValue = labels[selectedIndex];
        }
    }

    public static int ResolveCurrentZoneId()
    {
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        return metadata != null ? metadata.ZoneId : 0;
    }
}
}
