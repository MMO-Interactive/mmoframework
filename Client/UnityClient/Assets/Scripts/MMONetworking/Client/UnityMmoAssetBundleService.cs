using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public sealed class UnityMmoAssetBundleService : MonoBehaviour
{
    [Header("Asset Server")]
    [SerializeField] private string assetApiBaseUrl = "http://127.0.0.1:7095";
    [SerializeField] private string assetChannel = "live";
    [SerializeField] private string assetPlatform = string.Empty;
    [SerializeField] private bool preloadStartupBundles = false;
    [SerializeField] private bool autoLoadZoneBundles = true;
    [SerializeField] private string startupBundlesCsv = string.Empty;

    public string StatusText { get; private set; } = "Asset bundles idle.";
    public long ManifestId { get; private set; }
    public string ResolvedPlatform => ResolvePlatform();

    private readonly Dictionary<string, ManifestEntryData> _manifestEntries = new Dictionary<string, ManifestEntryData>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedBundleState> _loadedBundles = new Dictionary<string, LoadedBundleState>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AssetBundle>> _inflightLoads = new Dictionary<string, Task<AssetBundle>>(StringComparer.OrdinalIgnoreCase);
    private bool _manifestLoaded;
    private bool _startupRequested;
    private string _activeZoneBundleName = string.Empty;
    private string _activeZoneScenePath = string.Empty;

    private void Start()
    {
        if (preloadStartupBundles)
        {
            BeginEnsureStartupBundles();
        }
    }

    public void BeginEnsureStartupBundles()
    {
        if (_startupRequested)
        {
            return;
        }

        _startupRequested = true;
        _ = RunLoggedAsync(EnsureStartupBundlesAsync(), "startup bundle preload");
    }

    public void BeginEnsureZoneBundle(int zoneId, string explicitBundleName = null)
    {
        if (!autoLoadZoneBundles || zoneId <= 0)
        {
            Debug.Log("Ignoring zone bundle request. autoLoadZoneBundles=" + autoLoadZoneBundles + " zoneId=" + zoneId + ".");
            return;
        }

        Debug.Log("BeginEnsureZoneBundle zone=" + zoneId + " explicitBundleName='" + (string.IsNullOrWhiteSpace(explicitBundleName) ? "<empty>" : explicitBundleName) + "'.");
        _ = RunLoggedAsync(EnsureZoneBundleAsync(zoneId, explicitBundleName), "zone bundle load");
    }

    public async Task EnsureStartupBundlesAsync()
    {
        var startupBundles = ParseBundleList(startupBundlesCsv);
        if (startupBundles.Length == 0)
        {
            StatusText = "Waiting for zone bundle assignment.";
            return;
        }

        await EnsureManifestAsync();
        for (var i = 0; i < startupBundles.Length; i++)
        {
            await EnsureBundleAsync(startupBundles[i]);
        }
    }

    public Task<AssetBundle> EnsureZoneBundleAsync(int zoneId, string explicitBundleName = null)
    {
        var bundleName = string.IsNullOrWhiteSpace(explicitBundleName)
            ? "zone-" + zoneId
            : explicitBundleName.Trim();
        Debug.Log("EnsureZoneBundleAsync resolved bundle '" + bundleName + "' for zone " + zoneId + ".");
        return EnsureZoneBundleLoadedAsync(bundleName);
    }

    private async Task<AssetBundle> EnsureZoneBundleLoadedAsync(string bundleName)
    {
        var bundle = await EnsureBundleAsync(bundleName);
        if (bundle == null)
        {
            Debug.LogWarning("EnsureZoneBundleLoadedAsync got null bundle for '" + bundleName + "'.");
            return null;
        }

        await EnsureZoneSceneLoadedAsync(bundleName, bundle);
        return bundle;
    }

    public Task<AssetBundle> EnsureBundleAsync(string bundleName)
    {
        bundleName = (bundleName ?? string.Empty).Trim();
        if (bundleName.Length == 0)
        {
            return Task.FromResult<AssetBundle>(null);
        }

        if (_loadedBundles.TryGetValue(bundleName, out var existing) && existing.Bundle != null)
        {
            return Task.FromResult(existing.Bundle);
        }

        if (_inflightLoads.TryGetValue(bundleName, out var inflight))
        {
            return inflight;
        }

        var task = LoadBundleAsync(bundleName);
        _inflightLoads[bundleName] = task;
        return CompleteInflightAsync(bundleName, task);
    }

    private async Task<AssetBundle> CompleteInflightAsync(string bundleName, Task<AssetBundle> task)
    {
        try
        {
            return await task;
        }
        finally
        {
            _inflightLoads.Remove(bundleName);
        }
    }

    private async Task<AssetBundle> LoadBundleAsync(string bundleName)
    {
        await EnsureManifestAsync();

        if (!_manifestEntries.TryGetValue(bundleName, out var entry))
        {
            StatusText = "Bundle '" + bundleName + "' was not found in channel '" + assetChannel + "'.";
            Debug.LogWarning(StatusText);
            return null;
        }

        if (_loadedBundles.TryGetValue(bundleName, out var loadedState)
            && loadedState.Bundle != null
            && string.Equals(loadedState.ArtifactHash, entry.artifactHash, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Bundle '" + bundleName + "' already loaded.";
            return loadedState.Bundle;
        }

        var cachePath = GetBundleCachePath(entry);
        if (!File.Exists(cachePath))
        {
            var downloadUrl = BuildAbsoluteUrl(entry.downloadUrl);
            StatusText = "Downloading " + bundleName + "...";
            Debug.Log("Downloading bundle '" + bundleName + "' from " + downloadUrl + ".");
            using (var request = UnityWebRequest.Get(downloadUrl))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    StatusText = "Bundle download failed for " + bundleName + ": " + request.error;
                    Debug.LogWarning(StatusText);
                    return null;
                }

                File.WriteAllBytes(cachePath, request.downloadHandler.data);
                Debug.Log("Cached bundle '" + bundleName + "' at " + cachePath + ".");
            }
        }
        else
        {
            Debug.Log("Using cached bundle '" + bundleName + "' at " + cachePath + ".");
        }

        StatusText = "Loading " + bundleName + "...";
        Debug.Log("Loading bundle '" + bundleName + "' from file.");
        var loadRequest = AssetBundle.LoadFromFileAsync(cachePath);
        while (!loadRequest.isDone)
        {
            await Task.Yield();
        }

        var bundle = loadRequest.assetBundle;
        if (bundle == null)
        {
            StatusText = "Bundle load returned null for " + bundleName + ".";
            Debug.LogWarning(StatusText);
            return null;
        }

        if (_loadedBundles.TryGetValue(bundleName, out var prior) && prior.Bundle != null && prior.Bundle != bundle)
        {
            prior.Bundle.Unload(false);
        }

        _loadedBundles[bundleName] = new LoadedBundleState(entry.artifactHash, cachePath, bundle);
        StatusText = "Loaded " + bundleName + " (" + entry.version + ").";
        Debug.Log("Loaded bundle '" + bundleName + "' version '" + entry.version + "'.");
        return bundle;
    }

    private async Task EnsureZoneSceneLoadedAsync(string bundleName, AssetBundle bundle)
    {
        var scenePaths = bundle.GetAllScenePaths();
        if (scenePaths == null || scenePaths.Length == 0)
        {
            StatusText = "Loaded " + bundleName + " with no scenes.";
            Debug.LogWarning("Bundle '" + bundleName + "' loaded but contains no scenes.");
            return;
        }

        var nextScenePath = scenePaths[0];
        if (string.Equals(_activeZoneScenePath, nextScenePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_activeZoneBundleName, bundleName, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Zone scene already active: " + Path.GetFileNameWithoutExtension(nextScenePath) + ".";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_activeZoneScenePath))
        {
            var previousSceneName = Path.GetFileNameWithoutExtension(_activeZoneScenePath);
            var previousScene = SceneManager.GetSceneByName(previousSceneName);
            if (previousScene.IsValid() && previousScene.isLoaded)
            {
                StatusText = "Unloading " + previousSceneName + "...";
                var unload = SceneManager.UnloadSceneAsync(previousScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        await Task.Yield();
                    }
                }
            }
        }

        var nextSceneName = Path.GetFileNameWithoutExtension(nextScenePath);
        StatusText = "Loading zone scene " + nextSceneName + "...";
        Debug.Log("Loading zone scene '" + nextSceneName + "' from bundle '" + bundleName + "'.");
        var load = SceneManager.LoadSceneAsync(nextSceneName, LoadSceneMode.Additive);
        if (load == null)
        {
            StatusText = "Failed to load zone scene " + nextSceneName + ".";
            Debug.LogWarning(StatusText);
            return;
        }

        while (!load.isDone)
        {
            await Task.Yield();
        }

        _activeZoneBundleName = bundleName;
        _activeZoneScenePath = nextScenePath;
        StatusText = "Loaded zone scene " + nextSceneName + " from " + bundleName + ".";
        Debug.Log("Loaded zone scene '" + nextSceneName + "' from bundle '" + bundleName + "'.");
        UnityMmoWorldSceneController.RefreshFallbackWorldPresentation();
    }

    private async Task EnsureManifestAsync(bool forceRefresh = false)
    {
        if (_manifestLoaded && !forceRefresh)
        {
            return;
        }

        var manifestUrl = assetApiBaseUrl.TrimEnd('/') +
                          "/api/assets/channels/" +
                          UnityWebRequest.EscapeURL(assetChannel.Trim().ToLowerInvariant()) +
                          "/manifest?platform=" +
                          UnityWebRequest.EscapeURL(ResolvePlatform());

        StatusText = "Resolving asset manifest...";
        Debug.Log("Resolving asset manifest from " + manifestUrl + ".");
        using (var request = UnityWebRequest.Get(manifestUrl))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                StatusText = "Manifest request failed: " + request.error;
                Debug.LogWarning(StatusText);
                return;
            }

            var manifest = JsonUtility.FromJson<ManifestResponseData>(request.downloadHandler.text ?? string.Empty);
            if (manifest == null || manifest.entries == null)
            {
                StatusText = "Manifest response was empty.";
                Debug.LogWarning(StatusText);
                return;
            }

            _manifestEntries.Clear();
            for (var i = 0; i < manifest.entries.Length; i++)
            {
                var entry = manifest.entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                {
                    continue;
                }

                _manifestEntries[entry.bundleName.Trim()] = entry;
            }

            ManifestId = manifest.manifestId;
            _manifestLoaded = true;
            StatusText = "Resolved manifest " + ManifestId + " with " + _manifestEntries.Count + " entries.";
            Debug.Log("Resolved asset manifest " + ManifestId + " with " + _manifestEntries.Count + " entries for platform '" + ResolvePlatform() + "'.");
        }
    }

    private static async Task RunLoggedAsync(Task task, string operationName)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Debug.LogException(new InvalidOperationException("Asset bundle service failed during " + operationName + ".", ex));
        }
    }

    private static async Task<T> RunLoggedAsync<T>(Task<T> task, string operationName) where T : class
    {
        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            Debug.LogException(new InvalidOperationException("Asset bundle service failed during " + operationName + ".", ex));
            return null;
        }
    }

    private string GetBundleCachePath(ManifestEntryData entry)
    {
        var root = Path.Combine(
            Application.persistentDataPath,
            "asset-bundles",
            SanitizePathPart(assetChannel),
            SanitizePathPart(ResolvePlatform()));
        Directory.CreateDirectory(root);
        return Path.Combine(root, SanitizePathPart(entry.bundleName) + "-" + entry.artifactHash.ToLowerInvariant() + ".bundle");
    }

    private string BuildAbsoluteUrl(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return assetApiBaseUrl.TrimEnd('/');
        }

        if (downloadUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return downloadUrl;
        }

        if (!downloadUrl.StartsWith("/", StringComparison.Ordinal))
        {
            downloadUrl = "/" + downloadUrl;
        }

        return assetApiBaseUrl.TrimEnd('/') + downloadUrl;
    }

    private string ResolvePlatform()
    {
        if (!string.IsNullOrWhiteSpace(assetPlatform))
        {
            return assetPlatform.Trim().ToLowerInvariant();
        }

        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
            case RuntimePlatform.WindowsPlayer:
                return "windows";
            case RuntimePlatform.OSXEditor:
            case RuntimePlatform.OSXPlayer:
                return "osx";
            case RuntimePlatform.LinuxEditor:
            case RuntimePlatform.LinuxPlayer:
                return "linux";
            case RuntimePlatform.Android:
                return "android";
            case RuntimePlatform.IPhonePlayer:
                return "ios";
            case RuntimePlatform.WebGLPlayer:
                return "webgl";
            default:
                return "windows";
        }
    }

    private static string[] ParseBundleList(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        var parts = csv.Split(',');
        var list = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var value = parts[i].Trim();
            if (value.Length > 0)
            {
                list.Add(value);
            }
        }

        return list.ToArray();
    }

    private static string SanitizePathPart(string value)
    {
        var next = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim().ToLowerInvariant();
        for (var i = 0; i < Path.GetInvalidFileNameChars().Length; i++)
        {
            next = next.Replace(Path.GetInvalidFileNameChars()[i], '_');
        }

        return next;
    }

    private sealed class LoadedBundleState
    {
        public LoadedBundleState(string artifactHash, string path, AssetBundle bundle)
        {
            ArtifactHash = artifactHash;
            Path = path;
            Bundle = bundle;
        }

        public string ArtifactHash { get; }
        public string Path { get; }
        public AssetBundle Bundle { get; }
    }

    [Serializable]
    private sealed class ManifestResponseData
    {
        public string channel;
        public long manifestId;
        public string generatedAtUtc;
        public ManifestEntryData[] entries;
    }

    [Serializable]
    private sealed class ManifestEntryData
    {
        public string bundleName;
        public string platform;
        public string version;
        public string artifactHash;
        public string unityVersion;
        public string downloadUrl;
    }
}
}
