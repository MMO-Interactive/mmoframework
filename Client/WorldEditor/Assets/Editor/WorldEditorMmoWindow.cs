using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public sealed class WorldEditorMmoWindow : EditorWindow
{
    [MenuItem("Rise of Heroes/MMO Editor/Open Editor", priority = 1)]
    public static void Open()
    {
        var window = GetWindow<WorldEditorMmoWindow>("MMORPG Editor");
        window.minSize = new Vector2(460f, 420f);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        WorldEditorWorkspace.UpdatePolling(Repaint);
    }

    private void OnGUI()
    {
        var validation = WorldEditorWorkspace.ValidateActiveScene();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Rise of Heroes MMORPG Editor", WorldEditorEditorStyles.HeroStyle);
        EditorGUILayout.LabelField("Professional world-authoring workspace for zones, terrain, live sync, and publishing.", WorldEditorEditorStyles.MutedLabelStyle);
        EditorGUILayout.Space(8f);

        using (WorldEditorEditorStyles.Panel("Workspace"))
        {
            EditorGUILayout.LabelField("Active Scene", validation.Scene.name);
            if (validation.Metadata != null)
            {
                EditorGUILayout.LabelField("Zone", validation.Metadata.ZoneId + " | " + validation.Metadata.ZoneName);
                EditorGUILayout.LabelField("Bundle", string.IsNullOrWhiteSpace(validation.Metadata.AssetBundleName) ? "(unassigned)" : validation.Metadata.AssetBundleName);
            }
            else
            {
                EditorGUILayout.HelpBox(validation.Message, MessageType.Warning);
                if (GUILayout.Button("Create Zone Metadata"))
                {
                    WorldEditorWorkspace.CreateZoneMetadata();
                }
            }
        }

        using (WorldEditorEditorStyles.Panel("Live Connection"))
        {
            EditorGUILayout.LabelField(WorldEditorWorkspace.LiveStatus, WorldEditorEditorStyles.MutedLabelStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Live Data"))
                {
                    _ = WorldEditorWorkspace.RefreshLiveDataAsync(Repaint);
                }

                GUI.enabled = WorldEditorWorkspace.Definitions != null;
                if (GUILayout.Button("Import Zone"))
                {
                    try
                    {
                        WorldEditorWorkspace.ImportSelectedZone();
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
                GUI.enabled = true;
            }
        }

        using (WorldEditorEditorStyles.Panel("Tools", "Focused tools replace the old all-in-one prototype window."))
        {
            if (GUILayout.Button("Open Live Operations"))
            {
                WorldEditorLiveOperationsWindow.Open();
            }

            if (GUILayout.Button("Open Zone Authoring"))
            {
                WorldEditorZoneAuthoringWindow.Open();
            }

            if (GUILayout.Button("Open Terrain Tool"))
            {
                WorldEditorTerrainToolWindow.Open();
            }

            if (GUILayout.Button("Open Publishing Tool"))
            {
                WorldEditorPublishingWindow.Open();
            }
        }
    }
}
}
