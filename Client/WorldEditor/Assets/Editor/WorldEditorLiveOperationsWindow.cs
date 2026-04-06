using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public sealed class WorldEditorLiveOperationsWindow : EditorWindow
{
    [MenuItem("Rise of Heroes/MMO Editor/Tools/Live Operations", priority = 5)]
    public static void Open()
    {
        var window = GetWindow<WorldEditorLiveOperationsWindow>("MMO Live Ops");
        window.minSize = new Vector2(520f, 420f);
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
        using (WorldEditorEditorStyles.Panel("Connection", "Server-backed live preview, import, and push operations."))
        {
            EditorGUI.BeginChangeCheck();
            var serverBaseUrl = EditorGUILayout.TextField("Dashboard URL", WorldEditorWorkspace.ServerBaseUrl);
            var selectedZoneId = EditorGUILayout.IntField("Live Zone Id", WorldEditorWorkspace.SelectedZoneId);
            var livePreviewEnabled = EditorGUILayout.Toggle("Scene Live Preview", WorldEditorWorkspace.LivePreviewEnabled);
            var autoPoll = EditorGUILayout.Toggle("Auto Poll", WorldEditorWorkspace.AutoPoll);
            if (EditorGUI.EndChangeCheck())
            {
                WorldEditorWorkspace.ServerBaseUrl = serverBaseUrl;
                WorldEditorWorkspace.SelectedZoneId = selectedZoneId;
                WorldEditorWorkspace.LivePreviewEnabled = livePreviewEnabled;
                WorldEditorWorkspace.AutoPoll = autoPoll;
            }

            EditorGUILayout.HelpBox(WorldEditorWorkspace.LiveStatus, MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !WorldEditorWorkspace.PollInFlight;
                if (GUILayout.Button("Refresh Live Data"))
                {
                    _ = WorldEditorWorkspace.RefreshLiveDataAsync(Repaint);
                }
                GUI.enabled = true;

                if (GUILayout.Button("Import Zone From Server"))
                {
                    try
                    {
                        WorldEditorWorkspace.ImportSelectedZone();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }

                if (GUILayout.Button("Push Scene To Server"))
                {
                    _ = WorldEditorWorkspace.PushSceneToServerAsync(Repaint);
                }
            }
        }

        var liveZone = WorldEditorWorkspace.Dashboard?.zones?.FirstOrDefault(entry => entry.zoneId == WorldEditorWorkspace.SelectedZoneId);
        using (WorldEditorEditorStyles.Panel("Live Zone Snapshot"))
        {
            if (liveZone == null)
            {
                EditorGUILayout.HelpBox("No live zone snapshot for the selected zone yet.", MessageType.Info);
            }
            else
            {
                var definitionNpcs = WorldEditorWorkspace.Definitions?.npcs?.Count(entry => entry.zoneId == liveZone.zoneId) ?? 0;
                var definitionNodes = WorldEditorWorkspace.Definitions?.nodes?.Count(entry => entry.zoneId == liveZone.zoneId) ?? 0;
                EditorGUILayout.LabelField("Zone", liveZone.zoneId + " | " + liveZone.name);
                EditorGUILayout.LabelField("Lifecycle", liveZone.lifecycleState + " | runtime " + liveZone.runtimeMode);
                EditorGUILayout.LabelField("Players", liveZone.activePlayers.ToString());
                EditorGUILayout.LabelField("Mobs", (liveZone.mobs?.Length ?? 0).ToString());
                EditorGUILayout.LabelField("NPC Definitions", definitionNpcs.ToString());
                EditorGUILayout.LabelField("Node Definitions", definitionNodes.ToString());
            }
        }
    }
}
}
