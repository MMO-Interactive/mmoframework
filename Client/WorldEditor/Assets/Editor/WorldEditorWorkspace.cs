using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
internal static class WorldEditorWorkspace
{
    private const string ServerBaseUrlKey = "RiseOfHeroes.WorldEditor.ServerBaseUrl";
    private const string SelectedZoneIdKey = "RiseOfHeroes.WorldEditor.SelectedZoneId";
    private const string AssetApiBaseUrlKey = "RiseOfHeroes.WorldEditor.AssetApiBaseUrl";
    private const string AssetChannelKey = "RiseOfHeroes.WorldEditor.AssetChannel";
    private const string AssetWriteApiKeyKey = "RiseOfHeroes.WorldEditor.AssetWriteApiKey";

    private static string _serverBaseUrl = EditorPrefs.GetString(ServerBaseUrlKey, "http://127.0.0.1:7080");
    private static int _selectedZoneId = EditorPrefs.GetInt(SelectedZoneIdKey, 1);
    private static string _assetApiBaseUrl = EditorPrefs.GetString(AssetApiBaseUrlKey, "http://127.0.0.1:7095");
    private static string _assetChannel = EditorPrefs.GetString(AssetChannelKey, "live");
    private static string _assetWriteApiKey = EditorPrefs.GetString(AssetWriteApiKeyKey, string.Empty);
    private static bool _livePreviewEnabled = true;
    private static bool _autoPoll = true;
    private static double _nextPollAt;
    private static bool _pollInFlight;
    private static string _liveStatus = "Disconnected.";
    private static LiveDashboardSnapshot _dashboard;
    private static LiveGameplayDefinitionsSnapshot _definitions;
    private static BuildTarget _publishBuildTarget = BuildTarget.StandaloneWindows64;

    public static string ServerBaseUrl
    {
        get => _serverBaseUrl;
        set
        {
            _serverBaseUrl = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:7080" : value.Trim();
            EditorPrefs.SetString(ServerBaseUrlKey, _serverBaseUrl);
        }
    }

    public static int SelectedZoneId
    {
        get => _selectedZoneId;
        set
        {
            _selectedZoneId = Mathf.Max(1, value);
            EditorPrefs.SetInt(SelectedZoneIdKey, _selectedZoneId);
        }
    }

    public static string AssetApiBaseUrl
    {
        get => _assetApiBaseUrl;
        set
        {
            _assetApiBaseUrl = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:7095" : value.Trim();
            EditorPrefs.SetString(AssetApiBaseUrlKey, _assetApiBaseUrl);
        }
    }

    public static string AssetChannel
    {
        get => _assetChannel;
        set
        {
            _assetChannel = string.IsNullOrWhiteSpace(value) ? "live" : value.Trim().ToLowerInvariant();
            EditorPrefs.SetString(AssetChannelKey, _assetChannel);
        }
    }

    public static string AssetWriteApiKey
    {
        get => _assetWriteApiKey;
        set
        {
            _assetWriteApiKey = value ?? string.Empty;
            EditorPrefs.SetString(AssetWriteApiKeyKey, _assetWriteApiKey);
        }
    }

    public static bool LivePreviewEnabled
    {
        get => _livePreviewEnabled;
        set
        {
            _livePreviewEnabled = value;
            ApplyLivePreview();
        }
    }

    public static bool AutoPoll
    {
        get => _autoPoll;
        set => _autoPoll = value;
    }

    public static string LiveStatus => _liveStatus;
    public static LiveDashboardSnapshot Dashboard => _dashboard;
    public static LiveGameplayDefinitionsSnapshot Definitions => _definitions;
    public static bool PollInFlight => _pollInFlight;
    public static BuildTarget PublishBuildTarget
    {
        get => _publishBuildTarget;
        set => _publishBuildTarget = value;
    }

    public static void ApplyLivePreview()
    {
        WorldEditorLivePreview.SetData(_livePreviewEnabled, _selectedZoneId, _dashboard, _definitions);
        SceneView.RepaintAll();
    }

    public static void UpdatePolling(Action repaint)
    {
        if (!_autoPoll || _pollInFlight || EditorApplication.timeSinceStartup < _nextPollAt)
        {
            return;
        }

        _ = RefreshLiveDataAsync(repaint);
    }

    public static async Task RefreshLiveDataAsync(Action repaint = null)
    {
        _pollInFlight = true;
        try
        {
            _dashboard = await WorldEditorServerClient.GetDashboardAsync(_serverBaseUrl);
            _definitions = await WorldEditorServerClient.GetGameplayDefinitionsAsync(_serverBaseUrl);
            _liveStatus = "Connected. Zones: " + (_dashboard?.zones?.Length ?? 0) + " | definitions loaded.";
            ApplyLivePreview();
        }
        catch (Exception ex)
        {
            _liveStatus = ex.Message;
        }
        finally
        {
            _pollInFlight = false;
            _nextPollAt = EditorApplication.timeSinceStartup + 1.0d;
            repaint?.Invoke();
        }
    }

    public static void ImportSelectedZone()
    {
        if (_definitions == null)
        {
            throw new InvalidOperationException("Load live data first.");
        }

        WorldEditorLiveSyncUtility.ImportZoneIntoActiveScene(_selectedZoneId, _definitions);
        _liveStatus = "Imported zone " + _selectedZoneId + " from server.";
    }

    public static async Task PushSceneToServerAsync(Action repaint = null)
    {
        if (_definitions == null)
        {
            _definitions = await WorldEditorServerClient.GetGameplayDefinitionsAsync(_serverBaseUrl);
        }

        var merged = WorldEditorLiveSyncUtility.BuildMergedSnapshotForScene(_selectedZoneId, _definitions);
        _definitions = await WorldEditorServerClient.PutGameplayDefinitionsAsync(_serverBaseUrl, merged);
        _liveStatus = "Pushed active scene zone data to server.";
        ApplyLivePreview();
        repaint?.Invoke();
    }

    public static async Task BuildAndPublishActiveZoneBundleAsync(Action repaint = null)
    {
        EditorSceneManager.SaveOpenScenes();
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        if (metadata == null)
        {
            throw new InvalidOperationException("The scene has no ZoneSceneMetadata.");
        }

        if (string.IsNullOrWhiteSpace(metadata.AssetBundleName))
        {
            throw new InvalidOperationException("ZoneSceneMetadata.AssetBundleName is required.");
        }

        WorldEditorAssetBundleExporter.AssignActiveSceneAsZoneBundle();
        var buildResult = WorldEditorAssetBundleExporter.BuildBundles(_publishBuildTarget);
        var bundleName = metadata.AssetBundleName.Trim();
        var bundleFilePath = Path.Combine(buildResult.OutputPath, bundleName);
        if (!File.Exists(bundleFilePath))
        {
            throw new FileNotFoundException("Built bundle file was not found.", bundleFilePath);
        }

        await WorldEditorServerClient.UploadZoneBundleAsync(
            _assetApiBaseUrl,
            bundleFilePath,
            bundleName,
            ResolveAssetPlatform(_publishBuildTarget),
            Application.unityVersion,
            "Published from WorldEditor",
            _assetChannel,
            _assetWriteApiKey);

        _liveStatus = "Published zone bundle '" + bundleName + "' directly to asset server channel '" + _assetChannel + "'.";
        await RefreshLiveDataAsync(repaint);
    }

    public static SceneValidationResult ValidateActiveScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(scene.path))
        {
            return new SceneValidationResult(scene, null, "Open and save a scene to enable MMO authoring tools.");
        }

        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        return new SceneValidationResult(scene, metadata, metadata == null ? "This scene has no ZoneSceneMetadata yet." : null);
    }

    public static void CreateZoneMetadata()
    {
        var root = new GameObject("Zone Metadata");
        Undo.RegisterCreatedObjectUndo(root, "Create Zone Metadata");
        var metadata = root.AddComponent<ZoneSceneMetadata>();
        metadata.ApplyStandardBounds(Vector3.zero);
        Selection.activeObject = root;
    }

    public static void CreateMarker<T>(string objectName) where T : Component
    {
        var markerObject = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(markerObject, "Create " + objectName);
        var sceneView = SceneView.lastActiveSceneView;
        var position = sceneView != null ? sceneView.pivot : Vector3.zero;
        markerObject.transform.position = WorldEditorTerrainUtility.SnapPositionToTerrain(new Vector3(position.x, 0f, position.z));
        markerObject.AddComponent<T>();
        Selection.activeObject = markerObject;
    }

    public static void DrawCollectionSection<T>(string title, T[] items, Action<T> drawRow) where T : Component
    {
        EditorGUILayout.LabelField(title + " (" + items.Length + ")", EditorStyles.boldLabel);
        if (items.Length == 0)
        {
            EditorGUILayout.HelpBox("None placed yet.", MessageType.None);
            return;
        }

        var ordered = items.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            drawRow(ordered[i]);
        }
    }

    public static void DrawGenericRow(string title, string subtitle, Vector3 position, GameObject target)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(subtitle);
        EditorGUILayout.LabelField("Pos", position.x.ToString("F1") + ", " + position.y.ToString("F1") + ", " + position.z.ToString("F1"));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select"))
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        if (GUILayout.Button("Frame"))
        {
            Selection.activeObject = target;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private static string ResolveAssetPlatform(BuildTarget buildTarget)
    {
        switch (buildTarget)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return "windows";
            case BuildTarget.StandaloneOSX:
                return "osx";
            case BuildTarget.StandaloneLinux64:
                return "linux";
            case BuildTarget.Android:
                return "android";
            case BuildTarget.iOS:
                return "ios";
            case BuildTarget.WebGL:
                return "webgl";
            default:
                return "windows";
        }
    }

    internal readonly struct SceneValidationResult
    {
        public SceneValidationResult(UnityEngine.SceneManagement.Scene scene, ZoneSceneMetadata metadata, string message)
        {
            Scene = scene;
            Metadata = metadata;
            Message = message;
        }

        public UnityEngine.SceneManagement.Scene Scene { get; }
        public ZoneSceneMetadata Metadata { get; }
        public string Message { get; }
    }
}
}
