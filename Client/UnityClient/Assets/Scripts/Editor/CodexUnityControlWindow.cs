#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public sealed class CodexUnityControlWindow : EditorWindow
{
    private const int AutoApplyActionLimit = 24;
    private const int HardActionLimit = 96;

    private readonly ConcurrentQueue<string> _bridgeProgressMessages = new();
    private readonly List<string> _log = new();
    private readonly List<string> _attachedImagePaths = new();
    private readonly object _bridgeLock = new();
    private Vector2 _scroll;
    private string _prompt = "Populate this scene with an MMORPG starter-area layout.";
    private bool _autoApplySmallBatches = true;
    private bool _isRunning;
    private bool _bridgeRequestCompleted;
    private CodexUnityResponse _pendingResponse;
    private string _pendingBridgeOutput;
    private string _pendingBridgeError;

    [MenuItem("Tools/MMO/Codex Unity Control")]
    public static void Open()
    {
        var window = GetWindow<CodexUnityControlWindow>("Codex Unity");
        window.minSize = new Vector2(520f, 420f);
    }

    private void OnEnable()
    {
        EditorApplication.update += PollCodexBridgeRequest;
    }

    private void OnDisable()
    {
        EditorApplication.update -= PollCodexBridgeRequest;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Codex Unity Editor Control", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Codex SDK is the default. This window can execute scene and asset actions returned by Codex. Use it only in a branch or with version control available.",
            MessageType.Warning);

        _autoApplySmallBatches = EditorGUILayout.ToggleLeft($"Auto-apply batches up to {AutoApplyActionLimit} actions", _autoApplySmallBatches);

        EditorGUILayout.LabelField("Prompt");
        _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.MinHeight(72));

        DrawAttachmentControls();

        using (new EditorGUI.DisabledScope(_isRunning))
        {
            if (GUILayout.Button("Send To Codex", GUILayout.Height(32)))
            {
                SendPromptToCodex();
            }
        }

        if (_pendingResponse != null)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Pending Codex Batch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"{_pendingResponse.message}\nActions: {_pendingResponse.Actions.Count}", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Pending Actions"))
            {
                ApplyResponse(_pendingResponse);
                _pendingResponse = null;
            }

            if (GUILayout.Button("Discard Pending Actions"))
            {
                Log("Discarded pending Codex batch.");
                _pendingResponse = null;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var line in _log)
        {
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.EndScrollView();
    }

    private void SendPromptToCodex()
    {
        _isRunning = true;
        lock (_bridgeLock)
        {
            _pendingBridgeOutput = null;
            _pendingBridgeError = null;
            _bridgeRequestCompleted = false;
        }

        Log("Sending prompt to Codex...");

        var validAttachments = GetValidAttachedImagePaths();
        if (validAttachments.Length > 0)
        {
            Log($"Including {validAttachments.Length} image attachment(s).");
        }

        var requestJson = BuildRequestJson(_prompt, validAttachments);
        Task.Run(() =>
        {
            string output = null;
            string error = null;
            try
            {
                output = RunCodexBridge(requestJson, EnqueueBridgeProgress);
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            lock (_bridgeLock)
            {
                _pendingBridgeOutput = output;
                _pendingBridgeError = error;
                _bridgeRequestCompleted = true;
            }
        });
    }

    private void DrawAttachmentControls()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Image Attachments", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Attach screenshots or reference images from disk. They are sent to Codex as local image inputs, not embedded into generated Unity assets.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Attach Image/Screenshot...", GUILayout.Height(24)))
        {
            var selectedPath = EditorUtility.OpenFilePanelWithFilters(
                "Attach image or screenshot for Codex",
                "",
                new[] { "Image files", "png,jpg,jpeg,bmp,gif,tga", "All files", "*" });

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                AddImageAttachment(selectedPath);
            }
        }

        using (new EditorGUI.DisabledScope(_attachedImagePaths.Count == 0))
        {
            if (GUILayout.Button("Clear Attachments", GUILayout.Height(24)))
            {
                _attachedImagePaths.Clear();
                Log("Cleared Codex image attachments.");
            }
        }

        EditorGUILayout.EndHorizontal();

        if (_attachedImagePaths.Count == 0)
        {
            EditorGUILayout.LabelField("No images attached.", EditorStyles.miniLabel);
            return;
        }

        for (var index = _attachedImagePaths.Count - 1; index >= 0; index--)
        {
            var path = _attachedImagePaths[index];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{Path.GetFileName(path)}  ({path})", EditorStyles.miniLabel);
            if (GUILayout.Button("Remove", GUILayout.Width(72)))
            {
                Log($"Removed Codex image attachment: {Path.GetFileName(path)}");
                _attachedImagePaths.RemoveAt(index);
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    private void AddImageAttachment(string selectedPath)
    {
        var fullPath = Path.GetFullPath(selectedPath);
        if (!File.Exists(fullPath))
        {
            Log($"Attachment skipped; file does not exist: {selectedPath}");
            return;
        }

        if (!IsSupportedImagePath(fullPath))
        {
            Log($"Attachment skipped; unsupported image extension: {selectedPath}");
            return;
        }

        if (_attachedImagePaths.Any(path => string.Equals(Path.GetFullPath(path), fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            Log($"Attachment already included: {Path.GetFileName(fullPath)}");
            return;
        }

        _attachedImagePaths.Add(fullPath);
        Log($"Attached image for Codex: {Path.GetFileName(fullPath)}");
    }

    private string[] GetValidAttachedImagePaths()
        => _attachedImagePaths
            .Select(path => Path.GetFullPath(path))
            .Where(path => File.Exists(path) && IsSupportedImagePath(path))
            .ToArray();

    private void PollCodexBridgeRequest()
    {
        var hadProgress = false;
        while (_bridgeProgressMessages.TryDequeue(out var progressMessage))
        {
            Log(progressMessage);
            hadProgress = true;
        }

        var completed = false;
        lock (_bridgeLock)
        {
            if (_bridgeRequestCompleted)
            {
                _bridgeRequestCompleted = false;
                completed = true;
            }
        }

        if (completed)
        {
            CompleteCodexBridgeRequest();
        }
        else if (hadProgress)
        {
            Repaint();
        }
    }

    private void CompleteCodexBridgeRequest()
    {
        string bridgeOutput;
        string bridgeError;
        lock (_bridgeLock)
        {
            bridgeOutput = _pendingBridgeOutput;
            bridgeError = _pendingBridgeError;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(bridgeError))
            {
                Log("Codex failed: " + bridgeError);
                return;
            }

            var response = CodexUnityResponse.Parse(bridgeOutput ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(response.message))
            {
                Log("Codex: " + response.message);
            }

            foreach (var planStep in response.Plan)
            {
                Log("Codex plan: " + planStep);
            }

            if (response.Actions.Count == 0)
            {
                Log("Codex returned no Unity actions.");
                return;
            }

            if (response.Actions.Count > HardActionLimit)
            {
                response = response.Limit(HardActionLimit, $"Batch trimmed to {HardActionLimit} actions.");
            }

            if (_autoApplySmallBatches && response.Actions.Count <= AutoApplyActionLimit)
            {
                ApplyResponse(response);
            }
            else
            {
                _pendingResponse = response;
                Log($"Pending Codex batch: {response.Actions.Count} actions. Review before applying.");
            }
        }
        catch (Exception exception)
        {
            Log("Codex failed: " + exception.Message);
            Debug.LogException(exception);
        }
        finally
        {
            lock (_bridgeLock)
            {
                _pendingBridgeOutput = null;
                _pendingBridgeError = null;
            }

            _isRunning = false;
            Repaint();
        }
    }

    private static string RunCodexBridge(string requestJson, Action<string> progress)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("MMO_UNITY_CODEX_NODE") ?? "node",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add(GetBridgeScriptPath());

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start Codex bridge process.");
        }

        process.StandardInput.Write(requestJson);
        process.StandardInput.Close();

        var errorBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            errorBuilder.AppendLine(args.Data);
            progress?.Invoke(args.Data.StartsWith("CODEX_STATUS ", StringComparison.Ordinal)
                ? args.Data.Substring("CODEX_STATUS ".Length)
                : args.Data);
        };
        process.BeginErrorReadLine();

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.CancelErrorRead();
        var error = errorBuilder.ToString();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Codex bridge failed with exit code {process.ExitCode}." : error.Trim());
        }

        return output;
    }

    private void EnqueueBridgeProgress(string message)
    {
        _bridgeProgressMessages.Enqueue("Codex activity: " + message);
    }

    private static string GetBridgeScriptPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("MMO_UNITY_CODEX_BRIDGE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CodexBridge", "unity-codex-bridge.mjs"));
    }

    private static bool IsSupportedImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRequestJson(string prompt, string[] attachedImagePaths)
    {
        var unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var repoRoot = Path.GetFullPath(Path.Combine(unityProjectRoot, "..", ".."));
        var activeScene = SceneManager.GetActiveScene().path;
        var selectedObjects = Selection.gameObjects.Select(go => go.name).ToArray();
        var sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.scene.IsValid())
            .Select(go => go.name)
            .Distinct()
            .Take(160)
            .ToArray();
        var knownScenes = AssetDatabase.FindAssets("t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Take(32)
            .ToArray();

        return JsonUtility.ToJson(new CodexUnityRequest
        {
            prompt = prompt,
            unityProjectRoot = unityProjectRoot,
            repoRoot = repoRoot,
            activeScenePath = activeScene,
            selectedObjects = selectedObjects,
            sceneObjects = sceneObjects,
            knownScenes = knownScenes,
            attachedImagePaths = attachedImagePaths ?? Array.Empty<string>()
        });
    }

    private void ApplyResponse(CodexUnityResponse response)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Apply Codex Unity Actions");
        var undoGroup = Undo.GetCurrentGroup();

        var applied = 0;
        var index = 0;
        foreach (var action in response.Actions)
        {
            index++;
            try
            {
                Log($"Applying action {index}/{response.Actions.Count}: {DescribeAction(action)}");
                ExecuteAction(action);
                applied++;
            }
            catch (Exception exception)
            {
                Log($"Action failed ({action.type}): {exception.Message}");
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log($"Applied {applied}/{response.Actions.Count} Codex Unity action(s).");
    }

    private static string DescribeAction(CodexUnityAction action)
    {
        var target = string.IsNullOrWhiteSpace(action.name) ? action.parent : action.name;
        return string.IsNullOrWhiteSpace(target) ? action.type : $"{action.type} -> {target}";
    }

    private void ExecuteAction(CodexUnityAction action)
    {
        switch (action.type)
        {
            case "create_game_object":
                CreateGameObject(action);
                break;
            case "set_transform":
                SetTransform(action);
                break;
            case "add_component":
                AddComponent(action);
                break;
            case "set_parent":
                SetParent(action);
                break;
            case "rename_object":
                RenameObject(action);
                break;
            case "set_active":
                SetActive(action);
                break;
            case "destroy_object":
                DestroyObject(action);
                break;
            case "create_canvas":
                CreateCanvas(action);
                break;
            case "create_ui_panel":
                CreateUiPanel(action);
                break;
            case "create_ui_text":
                CreateUiText(action);
                break;
            case "create_ui_button":
                CreateUiButton(action);
                break;
            case "create_ui_image":
                CreateUiImage(action);
                break;
            case "set_ui_text":
                SetUiText(action);
                break;
            case "set_ui_color":
                SetUiColor(action);
                break;
            case "set_ui_rect":
                SetUiRect(action);
                break;
            case "create_material":
                CreateMaterial(action);
                break;
            case "assign_material":
                AssignMaterial(action);
                break;
            case "create_folder":
                CreateFolder(action.assetPath);
                break;
            case "create_csharp_script":
                CreateCSharpScript(action);
                break;
            case "open_scene":
                EditorSceneManager.OpenScene(action.scenePath);
                break;
            case "save_scene":
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                break;
            case "execute_menu_item":
                EditorApplication.ExecuteMenuItem(action.menuPath);
                break;
            case "select_object":
                Selection.activeGameObject = FindObjectByName(action.name);
                break;
            default:
                throw new NotSupportedException("Unsupported action type: " + action.type);
        }
    }

    private static void CreateGameObject(CodexUnityAction action)
    {
        GameObject go;
        if (Enum.TryParse(action.primitive, ignoreCase: true, out PrimitiveType primitive))
        {
            go = GameObject.CreatePrimitive(primitive);
            Undo.RegisterCreatedObjectUndo(go, "Create Codex primitive");
        }
        else
        {
            go = new GameObject(string.IsNullOrWhiteSpace(action.name) ? "Codex GameObject" : action.name);
            Undo.RegisterCreatedObjectUndo(go, "Create Codex object");
        }

        go.name = string.IsNullOrWhiteSpace(action.name) ? go.name : action.name;
        ApplyParent(go.transform, action.parent, worldPositionStays: true);
        ApplyTransform(go.transform, action);
        Selection.activeGameObject = go;
    }

    private static void SetTransform(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        Undo.RecordObject(go.transform, "Codex set transform");
        ApplyTransform(go.transform, action);
    }

    private static void ApplyTransform(Transform transform, CodexUnityAction action)
    {
        transform.position = new Vector3(action.x, action.y, action.z);
        transform.rotation = Quaternion.Euler(action.rx, action.ry, action.rz);
        var scale = new Vector3(action.sx == 0 ? 1 : action.sx, action.sy == 0 ? 1 : action.sy, action.sz == 0 ? 1 : action.sz);
        transform.localScale = scale;
    }

    private static void SetParent(CodexUnityAction action)
    {
        var child = FindObjectByName(action.name);
        if (child == null)
        {
            throw new InvalidOperationException("Child object not found: " + action.name);
        }

        Undo.SetTransformParent(child.transform, ResolveParent(action.parent), "Codex set parent");
    }

    private static void RenameObject(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        if (string.IsNullOrWhiteSpace(action.newName))
        {
            throw new InvalidOperationException("newName is required for rename_object.");
        }

        Undo.RecordObject(go, "Codex rename object");
        go.name = action.newName;
        Selection.activeGameObject = go;
    }

    private static void SetActive(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        Undo.RecordObject(go, "Codex set active");
        go.SetActive(action.active);
        Selection.activeGameObject = go;
    }

    private static void DestroyObject(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        Undo.DestroyObjectImmediate(go);
    }

    private static void AddComponent(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        var componentType = ResolveUnityType(action.component);
        if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            throw new InvalidOperationException("Component type not found: " + action.component);
        }

        Undo.AddComponent(go, componentType);
    }

    private static void CreateCanvas(CodexUnityAction action)
    {
        var name = string.IsNullOrWhiteSpace(action.name) ? "Codex Canvas" : action.name;
        var existing = FindObjectByName(name);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            return;
        }

        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(go, "Create Codex canvas");

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(action.width > 0 ? action.width : 1920f, action.height > 0 ? action.height : 1080f);

        EnsureEventSystem();
        Selection.activeGameObject = go;
    }

    private static void CreateUiPanel(CodexUnityAction action)
    {
        var go = CreateUiObject(action, "Codex Panel", typeof(Image));
        var image = go.GetComponent<Image>();
        image.color = ResolveColor(action, new Color(0.05f, 0.05f, 0.05f, 0.85f));
        Selection.activeGameObject = go;
    }

    private static void CreateUiImage(CodexUnityAction action)
    {
        var go = CreateUiObject(action, "Codex Image", typeof(Image));
        var image = go.GetComponent<Image>();
        image.color = ResolveColor(action, Color.white);
        Selection.activeGameObject = go;
    }

    private static void CreateUiText(CodexUnityAction action)
    {
        var go = CreateUiObject(action, "Codex Text", typeof(Text));
        ConfigureText(go.GetComponent<Text>(), action, action.text);
        Selection.activeGameObject = go;
    }

    private static void CreateUiButton(CodexUnityAction action)
    {
        var go = CreateUiObject(action, "Codex Button", typeof(Image), typeof(Button));
        go.GetComponent<Image>().color = ResolveColor(action, new Color(0.18f, 0.28f, 0.42f, 1f));

        var labelAction = action.CloneForChild(action.name + " Label");
        labelAction.parent = go.name;
        labelAction.anchorMinX = 0f;
        labelAction.anchorMinY = 0f;
        labelAction.anchorMaxX = 1f;
        labelAction.anchorMaxY = 1f;
        labelAction.anchoredX = 0f;
        labelAction.anchoredY = 0f;
        labelAction.width = 0f;
        labelAction.height = 0f;
        labelAction.r = 1f;
        labelAction.g = 1f;
        labelAction.b = 1f;
        labelAction.a = 1f;
        labelAction.alignment = "MiddleCenter";
        labelAction.text = string.IsNullOrWhiteSpace(action.text) ? action.name : action.text;
        var label = CreateUiObject(labelAction, "Label", typeof(Text));
        ConfigureText(label.GetComponent<Text>(), labelAction, labelAction.text);

        Selection.activeGameObject = go;
    }

    private static void SetUiText(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        var text = go.GetComponent<Text>();
        if (text == null)
        {
            throw new InvalidOperationException("Object has no UnityEngine.UI.Text component: " + action.name);
        }

        Undo.RecordObject(text, "Codex set UI text");
        if (!string.IsNullOrWhiteSpace(action.text))
        {
            text.text = action.text;
        }

        text.font = text.font == null ? GetDefaultUiFont() : text.font;
        if (action.fontSize > 0)
        {
            text.fontSize = action.fontSize;
        }

        if (!string.IsNullOrWhiteSpace(action.alignment) && Enum.TryParse(action.alignment, ignoreCase: true, out TextAnchor alignment))
        {
            text.alignment = alignment;
        }

        var hasColor = action.r != 0f || action.g != 0f || action.b != 0f || action.a != 0f;
        if (hasColor)
        {
            text.color = ResolveColor(action, text.color);
        }

        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        EditorUtility.SetDirty(text);
        Selection.activeGameObject = go;
    }

    private static void SetUiColor(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        var graphic = go.GetComponent<Graphic>();
        if (graphic != null)
        {
            Undo.RecordObject(graphic, "Codex set UI color");
            graphic.color = ResolveColor(action, graphic.color);
            EditorUtility.SetDirty(graphic);
            Selection.activeGameObject = go;
            return;
        }

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            Undo.RecordObject(renderer, "Codex set renderer color");
            var material = renderer.sharedMaterial;
            if (material == null)
            {
                throw new InvalidOperationException("Object has no shared material to recolor: " + action.name);
            }

            Undo.RecordObject(material, "Codex set material color");
            material.color = ResolveColor(action, material.color);
            EditorUtility.SetDirty(material);
            Selection.activeGameObject = go;
            return;
        }

        throw new InvalidOperationException("Object has no UI Graphic or Renderer to recolor: " + action.name);
    }

    private static void SetUiRect(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        if (go.transform is not RectTransform rectTransform)
        {
            throw new InvalidOperationException("Object is not a RectTransform UI object: " + action.name);
        }

        Undo.RecordObject(rectTransform, "Codex set UI rect");
        ApplyRectTransform(rectTransform, action);
        EditorUtility.SetDirty(rectTransform);
        Selection.activeGameObject = go;
    }

    private static GameObject CreateUiObject(CodexUnityAction action, string fallbackName, params Type[] components)
    {
        var name = string.IsNullOrWhiteSpace(action.name) ? fallbackName : action.name;
        var types = new[] { typeof(RectTransform) }.Concat(components).Distinct().ToArray();
        var go = new GameObject(name, types);
        Undo.RegisterCreatedObjectUndo(go, "Create Codex UI object");

        var parent = ResolveParent(action.parent) ?? EnsureDefaultCanvas().transform;
        Undo.SetTransformParent(go.transform, parent, "Parent Codex UI object");
        ApplyRectTransform((RectTransform)go.transform, action);
        return go;
    }

    private static void ConfigureText(Text text, CodexUnityAction action, string value)
    {
        text.text = string.IsNullOrWhiteSpace(value) ? action.name : value;
        text.font = GetDefaultUiFont();
        text.fontSize = action.fontSize > 0 ? action.fontSize : 24;
        text.color = ResolveColor(action, Color.white);
        text.alignment = Enum.TryParse(action.alignment, ignoreCase: true, out TextAnchor alignment) ? alignment : TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
    }

    private static Font GetDefaultUiFont()
        => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

    private static GameObject EnsureDefaultCanvas()
    {
        var existingCanvas = Resources.FindObjectsOfTypeAll<Canvas>().FirstOrDefault(canvas => canvas.gameObject.scene.IsValid());
        if (existingCanvas != null)
        {
            return existingCanvas.gameObject;
        }

        var action = new CodexUnityAction { name = "Codex Canvas", width = 1920f, height = 1080f };
        CreateCanvas(action);
        return FindObjectByName(action.name);
    }

    private static void EnsureEventSystem()
    {
        if (Resources.FindObjectsOfTypeAll<EventSystem>().Any(eventSystem => eventSystem.gameObject.scene.IsValid()))
        {
            return;
        }

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
    }

    private static void ApplyRectTransform(RectTransform rectTransform, CodexUnityAction action)
    {
        ApplyUiLayoutPreset(action);

        rectTransform.anchorMin = new Vector2(action.anchorMinX, action.anchorMinY);
        rectTransform.anchorMax = new Vector2(action.anchorMaxX, action.anchorMaxY);
        rectTransform.pivot = new Vector2(action.pivotX == 0f && action.pivotY == 0f ? 0.5f : action.pivotX, action.pivotX == 0f && action.pivotY == 0f ? 0.5f : action.pivotY);
        rectTransform.anchoredPosition = new Vector2(action.anchoredX, action.anchoredY);
        rectTransform.sizeDelta = new Vector2(action.width, action.height);
        rectTransform.localRotation = Quaternion.Euler(action.rx, action.ry, action.rz);
        rectTransform.localScale = new Vector3(action.sx == 0 ? 1 : action.sx, action.sy == 0 ? 1 : action.sy, action.sz == 0 ? 1 : action.sz);
    }

    private static void ApplyUiLayoutPreset(CodexUnityAction action)
    {
        var preset = (action.layout ?? string.Empty).Trim().ToLowerInvariant();
        switch (preset)
        {
            case "stretch":
            case "full":
            case "fullscreen":
                action.anchorMinX = 0f;
                action.anchorMinY = 0f;
                action.anchorMaxX = 1f;
                action.anchorMaxY = 1f;
                action.pivotX = 0.5f;
                action.pivotY = 0.5f;
                action.anchoredX = 0f;
                action.anchoredY = 0f;
                action.width = 0f;
                action.height = 0f;
                break;
            case "topbar":
                action.anchorMinX = 0f;
                action.anchorMinY = 1f;
                action.anchorMaxX = 1f;
                action.anchorMaxY = 1f;
                action.pivotX = 0.5f;
                action.pivotY = 1f;
                action.anchoredX = 0f;
                action.anchoredY = -Mathf.Max(0f, action.marginTop);
                action.width = -(Mathf.Max(0f, action.marginLeft) + Mathf.Max(0f, action.marginRight));
                action.height = action.height > 0f ? action.height : 72f;
                break;
            case "bottombar":
                action.anchorMinX = 0f;
                action.anchorMinY = 0f;
                action.anchorMaxX = 1f;
                action.anchorMaxY = 0f;
                action.pivotX = 0.5f;
                action.pivotY = 0f;
                action.anchoredX = 0f;
                action.anchoredY = Mathf.Max(0f, action.marginBottom);
                action.width = -(Mathf.Max(0f, action.marginLeft) + Mathf.Max(0f, action.marginRight));
                action.height = action.height > 0f ? action.height : 88f;
                break;
            case "left":
            case "leftpanel":
                action.anchorMinX = 0f;
                action.anchorMinY = 0.5f;
                action.anchorMaxX = 0f;
                action.anchorMaxY = 0.5f;
                action.pivotX = 0f;
                action.pivotY = 0.5f;
                action.anchoredX = Mathf.Max(0f, action.marginLeft);
                action.width = action.width > 0f ? action.width : 320f;
                action.height = action.height > 0f ? action.height : 420f;
                break;
            case "right":
            case "rightpanel":
                action.anchorMinX = 1f;
                action.anchorMinY = 0.5f;
                action.anchorMaxX = 1f;
                action.anchorMaxY = 0.5f;
                action.pivotX = 1f;
                action.pivotY = 0.5f;
                action.anchoredX = -Mathf.Max(0f, action.marginRight);
                action.width = action.width > 0f ? action.width : 320f;
                action.height = action.height > 0f ? action.height : 420f;
                break;
            case "center":
            case "modal":
                action.anchorMinX = 0.5f;
                action.anchorMinY = 0.5f;
                action.anchorMaxX = 0.5f;
                action.anchorMaxY = 0.5f;
                action.pivotX = 0.5f;
                action.pivotY = 0.5f;
                action.width = action.width > 0f ? action.width : 640f;
                action.height = action.height > 0f ? action.height : 360f;
                break;
        }
    }

    private static Color ResolveColor(CodexUnityAction action, Color fallback)
    {
        if (action.r == 0f && action.g == 0f && action.b == 0f && action.a == 0f)
        {
            return fallback;
        }

        return new Color(action.r, action.g, action.b, action.a <= 0 ? 1f : action.a);
    }

    private static void ApplyParent(Transform transform, string parentName, bool worldPositionStays)
    {
        var parent = ResolveParent(parentName);
        if (parent != null)
        {
            Undo.SetTransformParent(transform, parent, "Parent Codex object");
        }
    }

    private static Transform ResolveParent(string parentName)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            return null;
        }

        var parent = FindObjectByName(parentName);
        if (parent == null)
        {
            throw new InvalidOperationException("Parent object not found: " + parentName);
        }

        return parent.transform;
    }

    private static void CreateMaterial(CodexUnityAction action)
    {
        var assetPath = NormalizeAssetPath(action.assetPath);
        CreateFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null)
        {
            Undo.RecordObject(existing, "Update Codex material");
            existing.color = new Color(action.r, action.g, action.b, action.a <= 0 ? 1 : action.a);
            EditorUtility.SetDirty(existing);
            return;
        }

        var material = new Material(shader)
        {
            color = new Color(action.r, action.g, action.b, action.a <= 0 ? 1 : action.a)
        };

        AssetDatabase.CreateAsset(material, assetPath);
    }

    private static void CreateCSharpScript(CodexUnityAction action)
    {
        var assetPath = NormalizeAssetPath(action.assetPath);
        if (!assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("C# script path must end with .cs: " + assetPath);
        }

        if (string.IsNullOrWhiteSpace(action.content))
        {
            throw new InvalidOperationException("C# script content is required for: " + assetPath);
        }

        CreateFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("C# script path escaped Unity project root: " + assetPath);
        }

        if (File.Exists(fullPath) && !action.overwrite)
        {
            throw new InvalidOperationException("C# script already exists and overwrite is false: " + assetPath);
        }

        File.WriteAllText(fullPath, NormalizeCSharpSource(action.content), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
    }

    private static string NormalizeCSharpSource(string source)
        => source.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

    private static void AssignMaterial(CodexUnityAction action)
    {
        var go = FindObjectByName(action.name);
        if (go == null)
        {
            throw new InvalidOperationException("Object not found: " + action.name);
        }

        var renderer = go.GetComponent<Renderer>();
        if (renderer == null)
        {
            throw new InvalidOperationException("Object has no Renderer: " + action.name);
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(NormalizeAssetPath(action.assetPath));
        if (material == null)
        {
            throw new InvalidOperationException("Material not found: " + action.assetPath);
        }

        Undo.RecordObject(renderer, "Codex assign material");
        renderer.sharedMaterial = material;
    }

    private static void CreateFolder(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        assetPath = NormalizeAssetPath(assetPath);
        var parts = assetPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            throw new InvalidOperationException("Folder must be under Assets: " + assetPath);
        }

        var current = "Assets";
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new ArgumentException("Asset path is required.");
        }

        assetPath = assetPath.Replace('\\', '/');
        if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) && assetPath != "Assets")
        {
            throw new InvalidOperationException("Asset path must start with Assets/: " + assetPath);
        }

        return assetPath;
    }

    private static GameObject FindObjectByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        return Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(go => go.name == objectName && go.scene.IsValid());
    }

    private static Type ResolveUnityType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return Type.GetType(typeName)
            ?? Type.GetType("UnityEngine." + typeName + ", UnityEngine")
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName) ?? assembly.GetType("UnityEngine." + typeName))
                .FirstOrDefault(type => type != null);
    }

    private void Log(string message)
    {
        _log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_log.Count > 200)
        {
            _log.RemoveAt(_log.Count - 1);
        }
    }

    [Serializable]
    private sealed class CodexUnityRequest
    {
        public string prompt;
        public string unityProjectRoot;
        public string repoRoot;
        public string activeScenePath;
        public string[] selectedObjects;
        public string[] sceneObjects;
        public string[] knownScenes;
        public string[] attachedImagePaths;
    }

    [Serializable]
    private sealed class CodexUnityResponse
    {
        public string message;
        public string[] plan;
        public CodexUnityAction[] actions;

        public IReadOnlyList<string> Plan => plan ?? Array.Empty<string>();
        public IReadOnlyList<CodexUnityAction> Actions => actions ?? Array.Empty<CodexUnityAction>();

        public static CodexUnityResponse Parse(string json)
        {
            var extracted = ExtractJsonObject(json);
            var response = JsonUtility.FromJson<CodexUnityResponse>(extracted);
            return response ?? new CodexUnityResponse { message = json, actions = Array.Empty<CodexUnityAction>() };
        }

        public CodexUnityResponse Limit(int maxActions, string note)
            => new()
            {
                message = string.IsNullOrWhiteSpace(message) ? note : message + "\n" + note,
                plan = Plan.ToArray(),
                actions = Actions.Take(maxActions).ToArray()
            };
    }

    [Serializable]
    private sealed class CodexUnityAction
    {
        public string type;
        public string name;
        public string newName;
        public string primitive;
        public string component;
        public string assetPath;
        public string scenePath;
        public string menuPath;
        public string parent;
        public string text;
        public string content;
        public string alignment;
        public string layout;
        public bool active = true;
        public bool overwrite = true;
        public float x;
        public float y;
        public float z;
        public float rx;
        public float ry;
        public float rz;
        public float sx;
        public float sy;
        public float sz;
        public float r;
        public float g;
        public float b;
        public float a;
        public float width;
        public float height;
        public float anchoredX;
        public float anchoredY;
        public float anchorMinX;
        public float anchorMinY;
        public float anchorMaxX;
        public float anchorMaxY;
        public float pivotX;
        public float pivotY;
        public float marginLeft;
        public float marginRight;
        public float marginTop;
        public float marginBottom;
        public int fontSize;

        public CodexUnityAction CloneForChild(string childName)
            => new()
            {
                type = type,
                name = childName,
                newName = newName,
                primitive = primitive,
                component = component,
                assetPath = assetPath,
                scenePath = scenePath,
                menuPath = menuPath,
                parent = parent,
                text = text,
                content = content,
                alignment = alignment,
                layout = layout,
                active = active,
                overwrite = overwrite,
                x = x,
                y = y,
                z = z,
                rx = rx,
                ry = ry,
                rz = rz,
                sx = sx,
                sy = sy,
                sz = sz,
                r = r,
                g = g,
                b = b,
                a = a,
                width = width,
                height = height,
                anchoredX = anchoredX,
                anchoredY = anchoredY,
                anchorMinX = anchorMinX,
                anchorMinY = anchorMinY,
                anchorMaxX = anchorMaxX,
                anchorMaxY = anchorMaxY,
                pivotX = pivotX,
                pivotY = pivotY,
                marginLeft = marginLeft,
                marginRight = marginRight,
                marginTop = marginTop,
                marginBottom = marginBottom,
                fontSize = fontSize
            };
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start ? text.Substring(start, end - start + 1) : "{\"message\":\"" + EscapeJson(text) + "\",\"actions\":[]}";
    }

    private static string EscapeJson(string value)
        => Regex.Replace(value ?? string.Empty, @"[""\\\u0000-\u001F]", match => match.Value switch
        {
            "\"" => "\\\"",
            "\\" => "\\\\",
            "\n" => "\\n",
            "\r" => "\\r",
            "\t" => "\\t",
            _ => "\\u" + ((int)match.Value[0]).ToString("x4", CultureInfo.InvariantCulture)
        });
}
#endif
