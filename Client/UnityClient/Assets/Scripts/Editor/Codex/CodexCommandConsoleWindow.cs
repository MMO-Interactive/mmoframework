using System;
using UnityEditor;
using UnityEngine;

namespace MMONetworking.UnityEditorCodex;

public sealed class CodexCommandConsoleWindow : EditorWindow
{
    private const string LastCommandPrefKey = "MMO.Codex.LastEditorCommand";

    [Serializable]
    private sealed class SerializableCommand
    {
        public string action = string.Empty;
        public string path = string.Empty;
        public string name = string.Empty;
        public string parentPath = string.Empty;
        public string componentType = string.Empty;
        public string propertyName = string.Empty;
        public string propertyValue = string.Empty;
        public int maxDepth = 8;
        public string prefabAssetPath = string.Empty;
        public string primitiveType = "Cube";
        public string tag = string.Empty;
        public int layer = -1;
        public bool isActive = true;
        public string newName = string.Empty;
        public string parentAssetFolder = "Assets";
        public string newFolderName = string.Empty;
        public bool removeSceneObjects = true;
        public string materialAssetPath = string.Empty;
        public string shaderName = "Standard";
        public string menuPath = string.Empty;
        public bool playMode = true;
        public string outputPath = "CodexScreenshots/shot.png";
        public int width = 1280;
        public int height = 720;
        public int superSize = 1;
        public Vector3 position = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;
    }

    [Serializable]
    private sealed class SerializableBatch
    {
        public SerializableCommand[] commands;
        public bool stopOnError = true;
        public bool captureAfterEachStep;
        public string screenshotFolder = "CodexScreenshots";
    }

    private string _commandJson = "{\n  \"action\": \"create_gameobject\",\n  \"name\": \"CodexSpawned\"\n}";
    private Vector2 _scroll;

    [MenuItem("MMO/Codex/Command Console")]
    public static void ShowWindow()
    {
        var window = GetWindow<CodexCommandConsoleWindow>("Codex Command Console");
        window.minSize = new Vector2(720f, 520f);
        window.Show();
    }

    [MenuItem("MMO/Codex/Print Capabilities")]
    public static void PrintCapabilities()
    {
        var capabilities = CodexUnityEditorBridge.GetCapabilities();
        Debug.Log("Codex Unity Editor Capabilities:\n- " + string.Join("\n- ", capabilities));
    }

    private void OnEnable()
    {
        if (EditorPrefs.HasKey(LastCommandPrefKey))
        {
            _commandJson = EditorPrefs.GetString(LastCommandPrefKey);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("JSON Command / Batch", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Supports a single command object or a batch object with a `commands` array. Batch mode can auto-capture screenshots after each step.",
            MessageType.Info);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _commandJson = EditorGUILayout.TextArea(_commandJson, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Run", GUILayout.Height(30f)))
        {
            RunCommand();
        }

        if (GUILayout.Button("Save As Last", GUILayout.Height(30f)))
        {
            EditorPrefs.SetString(LastCommandPrefKey, _commandJson);
            Debug.Log("Saved command preset.");
        }

        EditorGUILayout.EndHorizontal();
    }

    private void RunCommand()
    {
        var batch = JsonUtility.FromJson<SerializableBatch>(_commandJson);
        if (batch != null && batch.commands != null && batch.commands.Length > 0)
        {
            RunBatch(batch);
            EditorPrefs.SetString(LastCommandPrefKey, _commandJson);
            return;
        }

        SerializableCommand command;
        try
        {
            command = JsonUtility.FromJson<SerializableCommand>(_commandJson);
        }
        catch (Exception ex)
        {
            Debug.LogError("Invalid JSON command: " + ex.Message);
            return;
        }

        if (command == null)
        {
            Debug.LogError("Could not parse command JSON.");
            return;
        }

        EditorPrefs.SetString(LastCommandPrefKey, _commandJson);

        var result = Execute(command);
        LogResult(result);
    }

    private static void RunBatch(SerializableBatch batch)
    {
        for (var i = 0; i < batch.commands.Length; i++)
        {
            var command = batch.commands[i];
            var result = Execute(command);
            LogResult(result, "Step " + (i + 1) + ": ");

            if (!result.Ok && batch.stopOnError)
            {
                Debug.LogError("[Codex] Stopping batch due to step failure.");
                return;
            }

            if (batch.captureAfterEachStep)
            {
                var screenshotPath = batch.screenshotFolder.TrimEnd('/') + "/step_" + (i + 1).ToString("D3") + ".png";
                var screenshot = CodexUnityEditorBridge.CaptureSceneViewScreenshot(screenshotPath, 1280, 720);
                LogResult(screenshot, "Screenshot " + (i + 1) + ": ");
            }
        }
    }

    private static void LogResult(CodexCommandResult result, string prefix = "")
    {
        if (result.Ok)
        {
            Debug.Log("[Codex] " + prefix + result.Message);
        }
        else
        {
            Debug.LogError("[Codex] " + prefix + result.Message);
        }
    }

    private static CodexCommandResult Execute(SerializableCommand command)
    {
        switch (command.action)
        {
            case "open_scene":
                return CodexUnityEditorBridge.OpenScene(command.path);
            case "open_scene_additive":
                return CodexUnityEditorBridge.OpenSceneAdditive(command.path);
            case "create_scene_asset":
                return CodexUnityEditorBridge.CreateSceneAsset(command.name, command.parentAssetFolder);
            case "close_scene":
                return CodexUnityEditorBridge.CloseScene(command.path, command.removeSceneObjects);
            case "create_gameobject":
                return CodexUnityEditorBridge.CreateGameObject(command.name, command.parentPath);
            case "create_primitive":
                return CodexUnityEditorBridge.CreatePrimitive(command.primitiveType, command.name, command.parentPath);
            case "delete_gameobject":
                return CodexUnityEditorBridge.DeleteGameObject(command.path);
            case "duplicate_gameobject":
                return CodexUnityEditorBridge.DuplicateGameObject(command.path);
            case "rename_gameobject":
                return CodexUnityEditorBridge.RenameGameObject(command.path, command.newName);
            case "set_active":
                return CodexUnityEditorBridge.SetActive(command.path, command.isActive);
            case "instantiate_prefab":
                return CodexUnityEditorBridge.InstantiatePrefab(command.prefabAssetPath, command.parentPath);
            case "set_transform":
                return CodexUnityEditorBridge.SetTransform(command.path, command.position, command.rotation, command.scale);
            case "reset_transform":
                return CodexUnityEditorBridge.ResetTransform(command.path);
            case "add_component":
                return CodexUnityEditorBridge.AddComponent(command.path, command.componentType);
            case "set_component_property":
                return CodexUnityEditorBridge.SetComponentProperty(command.path, command.componentType, command.propertyName, command.propertyValue);
            case "get_component_property":
                return CodexUnityEditorBridge.GetComponentProperty(command.path, command.componentType, command.propertyName);
            case "remove_component":
                return CodexUnityEditorBridge.RemoveComponent(command.path, command.componentType);
            case "select_object":
                return CodexUnityEditorBridge.SelectObject(command.path);
            case "set_tag_layer":
                return CodexUnityEditorBridge.SetTagAndLayer(command.path, command.tag, command.layer);
            case "create_folder":
                return CodexUnityEditorBridge.CreateFolder(command.parentAssetFolder, command.newFolderName);
            case "move_to_parent":
                return CodexUnityEditorBridge.MoveToParent(command.path, command.parentPath);
            case "list_root_objects":
                return CodexUnityEditorBridge.ListRootObjects();
            case "list_children":
                return CodexUnityEditorBridge.ListChildren(command.path);
            case "create_material":
                return CodexUnityEditorBridge.CreateMaterial(command.materialAssetPath, command.shaderName);
            case "assign_material":
                return CodexUnityEditorBridge.AssignMaterial(command.path, command.materialAssetPath);
            case "focus_scene_view":
                return CodexUnityEditorBridge.FocusSceneView(command.path);
            case "run_menu_item":
                return CodexUnityEditorBridge.RunMenuItem(command.menuPath);
            case "set_play_mode":
                return CodexUnityEditorBridge.SetPlayMode(command.playMode);
            case "capture_scene_view_screenshot":
                return CodexUnityEditorBridge.CaptureSceneViewScreenshot(command.outputPath, command.width, command.height);
            case "capture_game_view_screenshot":
                return CodexUnityEditorBridge.CaptureGameViewScreenshot(command.outputPath, command.superSize);
            case "get_transform_info":
                return CodexUnityEditorBridge.GetTransformInfo(command.path);
            case "list_components":
                return CodexUnityEditorBridge.ListComponents(command.path);
            case "find_objects_by_name":
                return CodexUnityEditorBridge.FindObjectsByName(command.name);
            case "find_objects_by_component":
                return CodexUnityEditorBridge.FindObjectsByComponent(command.componentType);
            case "list_scene_hierarchy":
                return CodexUnityEditorBridge.ListSceneHierarchy(command.maxDepth);
            case "create_prefab_from_object":
                return CodexUnityEditorBridge.CreatePrefabFromObject(command.path, command.prefabAssetPath);
            case "list_open_scenes":
                return CodexUnityEditorBridge.ListOpenScenes();
            case "find_missing_scripts":
                return CodexUnityEditorBridge.FindMissingScripts();
            case "select_asset":
                return CodexUnityEditorBridge.SelectAsset(command.path);
            case "get_active_scene_info":
                return CodexUnityEditorBridge.GetActiveSceneInfo();
            case "save_active_scene":
                return CodexUnityEditorBridge.SaveActiveScene();
            case "mark_scene_dirty":
                return CodexUnityEditorBridge.MarkActiveSceneDirty();
            case "undo_last":
                return CodexUnityEditorBridge.UndoLastAction();
            case "redo_last":
                return CodexUnityEditorBridge.RedoLastAction();
            case "save_project":
                return CodexUnityEditorBridge.SaveProject();
            default:
                return CodexCommandResult.Fail("Unknown action: " + command.action);
        }
    }
}
