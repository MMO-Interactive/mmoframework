using System;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public sealed class WorldEditorPublishingWindow : EditorWindow
{
    [MenuItem("Rise of Heroes/MMO Editor/Tools/Publishing", priority = 8)]
    public static void Open()
    {
        var window = GetWindow<WorldEditorPublishingWindow>("Publishing");
        window.minSize = new Vector2(520f, 320f);
    }

    private void OnGUI()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();

        using (WorldEditorEditorStyles.Panel("Build / Export", "Export authoring metadata, assign bundle names, and publish zone bundles to the live server."))
        {
            EditorGUILayout.LabelField("Scene", scene.name);
            EditorGUILayout.LabelField("Bundle", metadata != null ? metadata.AssetBundleName : "(missing metadata)");
            if (metadata == null)
            {
                EditorGUILayout.HelpBox("Create ZoneSceneMetadata before publishing.", MessageType.Warning);
            }

            if (GUILayout.Button("Export Scene Authoring JSON"))
            {
                WorldEditorSceneExportUtility.ExportActiveSceneAuthoringJson();
            }

            if (GUILayout.Button("Assign Scene To Bundle"))
            {
                WorldEditorAssetBundleExporter.AssignActiveSceneAsZoneBundle();
            }

            if (GUILayout.Button("Build Windows Bundles + Metadata"))
            {
                EditorSceneManager.SaveOpenScenes();
                WorldEditorAssetBundleExporter.BuildWindowsBundles();
            }
        }

        using (WorldEditorEditorStyles.Panel("Publish"))
        {
            EditorGUI.BeginChangeCheck();
            var assetApiBaseUrl = EditorGUILayout.TextField("Asset API URL", WorldEditorWorkspace.AssetApiBaseUrl);
            var assetChannel = EditorGUILayout.TextField("Channel", WorldEditorWorkspace.AssetChannel);
            var assetWriteApiKey = EditorGUILayout.PasswordField("Write API Key", WorldEditorWorkspace.AssetWriteApiKey);
            WorldEditorWorkspace.PublishBuildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Publish Build Target", WorldEditorWorkspace.PublishBuildTarget);
            if (EditorGUI.EndChangeCheck())
            {
                WorldEditorWorkspace.AssetApiBaseUrl = assetApiBaseUrl;
                WorldEditorWorkspace.AssetChannel = assetChannel;
                WorldEditorWorkspace.AssetWriteApiKey = assetWriteApiKey;
            }

            EditorGUILayout.LabelField("Publish Target", WorldEditorWorkspace.AssetApiBaseUrl + " | channel " + WorldEditorWorkspace.AssetChannel);
            EditorGUILayout.HelpBox(WorldEditorWorkspace.LiveStatus, MessageType.None);
            if (GUILayout.Button("Build + Publish Active Zone Bundle"))
            {
                _ = PublishAsync();
            }
        }
    }

    private async System.Threading.Tasks.Task PublishAsync()
    {
        try
        {
            await WorldEditorWorkspace.BuildAndPublishActiveZoneBundleAsync(Repaint);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
}
