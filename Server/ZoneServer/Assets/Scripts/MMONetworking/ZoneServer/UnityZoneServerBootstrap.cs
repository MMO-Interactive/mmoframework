using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MMONetworking.ZoneServer
{
public sealed class UnityZoneServerBootstrap : MonoBehaviour
{
    [Header("Zone")]
    [SerializeField] private int zoneId = 1;
    [SerializeField] private string zoneName = "UnityZone";
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField] private int tcpPort = 7301;
    [SerializeField] private int udpPort = 7401;
    [SerializeField] private float minX = 0f;
    [SerializeField] private float maxX = 2000f;
    [SerializeField] private float minZ = 0f;
    [SerializeField] private float maxZ = 2000f;
    [SerializeField] private float prewarmMargin = 20f;
    [SerializeField] private int mobCount = 2;
    [SerializeField] private string npcDefinitionsFile = "";
    [SerializeField] private string mobSpawnDefinitionsFile = "";
    [Header("Control Plane")]
    [SerializeField] private string controlHost = "127.0.0.1";
    [SerializeField] private int controlPort = 7050;
    [SerializeField] private bool startOnAwake = true;
    [Header("Zone Asset Bundle")]
    [SerializeField] private bool loadZoneBundleAtStartup = true;
    [SerializeField] private string assetApiBaseUrl = "http://127.0.0.1:7095";
    [SerializeField] private string assetChannel = "live";
    [SerializeField] private string assetPlatform = "windows";
    [SerializeField] private string zoneBundleName = "";
    [SerializeField] private string zoneBundleHashOverride = "";
    [SerializeField] private bool loadSceneFromBundle = true;

    private UnityZoneServerRuntime _runtime;
    private Task _runTask;
    private string _status = "Idle";
    private AssetBundle _loadedBundle;
    private readonly List<string> _loadedSceneNames = new List<string>();
    private NpcDefinitionData[] _npcDefinitions = Array.Empty<NpcDefinitionData>();
    private MobSpawnDefinitionData[] _mobSpawnDefinitions = Array.Empty<MobSpawnDefinitionData>();

    public string Status => _status;

    private async void Awake()
    {
        ApplyCommandLineOverrides();
        if (loadZoneBundleAtStartup)
        {
            await TryLoadZoneBundleAsync();
        }
        if (startOnAwake)
        {
            await StartServerAsync();
        }
    }

    private void OnDestroy()
    {
        _runtime?.Dispose();
        _runtime = null;
        if (_loadedBundle != null)
        {
            _loadedBundle.Unload(false);
            _loadedBundle = null;
        }
    }

    public async Task StartServerAsync()
    {
        if (_runtime != null)
        {
            return;
        }

        var definition = new ZoneDefinition(zoneId, zoneName, host, tcpPort, udpPort, minX, maxX, minZ, maxZ);
        _runtime = new UnityZoneServerRuntime(definition, controlHost, controlPort, prewarmMargin, mobCount, _npcDefinitions, _mobSpawnDefinitions);
        _status = "Starting";
        _runTask = _runtime.RunAsync();
        await Task.Yield();
        _status = $"Listening TCP {tcpPort} / UDP {udpPort}";
    }

    private void Update()
    {
        if (_runtime != null)
        {
            _status = _runtime.Summary;
        }
    }

    private void ApplyCommandLineOverrides()
    {
        var args = Environment.GetCommandLineArgs();
        zoneId = ReadInt(args, "-mmo-zone-id", zoneId);
        zoneName = ReadString(args, "-mmo-zone-name", zoneName);
        host = ReadString(args, "-mmo-host", host);
        tcpPort = ReadInt(args, "-mmo-tcp-port", tcpPort);
        udpPort = ReadInt(args, "-mmo-udp-port", udpPort);
        minX = ReadFloat(args, "-mmo-min-x", minX);
        maxX = ReadFloat(args, "-mmo-max-x", maxX);
        minZ = ReadFloat(args, "-mmo-min-z", minZ);
        maxZ = ReadFloat(args, "-mmo-max-z", maxZ);
        prewarmMargin = ReadFloat(args, "-mmo-prewarm-margin", prewarmMargin);
        mobCount = ReadInt(args, "-mmo-mob-count", mobCount);
        npcDefinitionsFile = ReadString(args, "-mmo-npcs-file", npcDefinitionsFile);
        mobSpawnDefinitionsFile = ReadString(args, "-mmo-mob-spawns-file", mobSpawnDefinitionsFile);
        controlHost = ReadString(args, "-mmo-control-host", controlHost);
        controlPort = ReadInt(args, "-mmo-control-port", controlPort);
        assetApiBaseUrl = ReadString(args, "-mmo-asset-api-base-url", assetApiBaseUrl);
        assetChannel = ReadString(args, "-mmo-asset-channel", assetChannel);
        assetPlatform = ReadString(args, "-mmo-asset-platform", assetPlatform);
        zoneBundleName = ReadString(args, "-mmo-zone-bundle-name", zoneBundleName);
        zoneBundleHashOverride = ReadString(args, "-mmo-zone-bundle-hash", zoneBundleHashOverride);
        _npcDefinitions = LoadNpcDefinitions(npcDefinitionsFile, zoneId);
        _mobSpawnDefinitions = LoadMobSpawnDefinitions(mobSpawnDefinitionsFile, zoneId);
    }

    private async Task TryLoadZoneBundleAsync()
    {
        try
        {
            var hash = await ResolveZoneBundleHashAsync();
            if (string.IsNullOrWhiteSpace(hash))
            {
                Debug.LogWarning("Zone bundle hash could not be resolved; skipping bundle load.");
                return;
            }

            var bundlesDir = Path.Combine(Application.persistentDataPath, "zone-bundles");
            Directory.CreateDirectory(bundlesDir);
            var localPath = Path.Combine(bundlesDir, hash + ".bundle");
            if (!File.Exists(localPath))
            {
                var artifactUrl = assetApiBaseUrl.TrimEnd('/') + "/api/assets/artifacts/" + hash;
                using (var request = UnityWebRequest.Get(artifactUrl))
                {
                    var op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning("Failed to download zone asset bundle: " + request.error);
                        return;
                    }

                    File.WriteAllBytes(localPath, request.downloadHandler.data);
                }
            }

            var bundleRequest = AssetBundle.LoadFromFileAsync(localPath);
            while (!bundleRequest.isDone)
            {
                await Task.Yield();
            }

            _loadedBundle = bundleRequest.assetBundle;
            if (_loadedBundle == null)
            {
                Debug.LogWarning("Asset bundle load returned null for " + localPath);
                return;
            }

            if (loadSceneFromBundle)
            {
                await LoadBundleScenesAsync(_loadedBundle);
            }

            Debug.Log("Zone asset bundle loaded for zone " + zoneId + " from hash " + hash + ".");
        }
        catch (Exception ex)
        {
            Debug.LogError("Zone asset bundle bootstrap failed: " + ex);
        }
    }

    private async Task<string> ResolveZoneBundleHashAsync()
    {
        if (!string.IsNullOrWhiteSpace(zoneBundleHashOverride))
        {
            return zoneBundleHashOverride.Trim().ToLowerInvariant();
        }

        var resolvedBundleName = string.IsNullOrWhiteSpace(zoneBundleName)
            ? ("zone-" + zoneId)
            : zoneBundleName.Trim();
        var manifestUrl = assetApiBaseUrl.TrimEnd('/') + "/api/assets/channels/" + UnityWebRequest.EscapeURL(assetChannel) + "/manifest?platform=" + UnityWebRequest.EscapeURL(assetPlatform);
        using (var request = UnityWebRequest.Get(manifestUrl))
        {
            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Could not resolve zone manifest from Asset API: " + request.error);
                return string.Empty;
            }

            var json = request.downloadHandler.text ?? string.Empty;
            if (json.Length == 0)
            {
                return string.Empty;
            }

            var preferredPattern = new Regex("\"bundleName\"\\s*:\\s*\"" + Regex.Escape(resolvedBundleName) + "\"[\\s\\S]*?\"artifactHash\"\\s*:\\s*\"(?<hash>[a-fA-F0-9]{16,128})\"", RegexOptions.IgnoreCase);
            var preferredMatch = preferredPattern.Match(json);
            if (preferredMatch.Success)
            {
                return preferredMatch.Groups["hash"].Value.ToLowerInvariant();
            }

            var firstHash = Regex.Match(json, "\"artifactHash\"\\s*:\\s*\"(?<hash>[a-fA-F0-9]{16,128})\"", RegexOptions.IgnoreCase);
            return firstHash.Success ? firstHash.Groups["hash"].Value.ToLowerInvariant() : string.Empty;
        }
    }

    private async Task LoadBundleScenesAsync(AssetBundle bundle)
    {
        var scenePaths = bundle.GetAllScenePaths();
        for (var i = 0; i < scenePaths.Length; i++)
        {
            var path = scenePaths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var sceneName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(sceneName) || _loadedSceneNames.Contains(sceneName))
            {
                continue;
            }

            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                continue;
            }

            while (!load.isDone)
            {
                await Task.Yield();
            }

            _loadedSceneNames.Add(sceneName);
            Debug.Log("Loaded zone scene '" + sceneName + "' from asset bundle.");
        }
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var value = ReadString(args, name, null);
        int parsed;
        return value != null && int.TryParse(value, out parsed) ? parsed : fallback;
    }

    private static float ReadFloat(string[] args, string name, float fallback)
    {
        var value = ReadString(args, name, null);
        float parsed;
        return value != null && float.TryParse(value, out parsed) ? parsed : fallback;
    }

    private static string ReadString(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static NpcDefinitionData[] LoadNpcDefinitions(string path, int currentZoneId)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<NpcDefinitionData>();
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<NpcDefinitionData>();
            }

            var file = JsonUtility.FromJson<NpcDefinitionFileData>(json);
            if (file == null || file.npcs == null)
            {
                return Array.Empty<NpcDefinitionData>();
            }

            var filtered = new List<NpcDefinitionData>();
            for (var i = 0; i < file.npcs.Length; i++)
            {
                var npc = file.npcs[i];
                if (npc != null && npc.zoneId == currentZoneId)
                {
                    if (npc.services == null)
                    {
                        npc.services = Array.Empty<string>();
                    }

                    if (npc.serviceOptions == null)
                    {
                        npc.serviceOptions = Array.Empty<NpcServiceDefinitionData>();
                    }

                    filtered.Add(npc);
                }
            }

            return filtered.ToArray();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to load NPC definitions file: " + ex.Message);
            return Array.Empty<NpcDefinitionData>();
        }
    }

    private static MobSpawnDefinitionData[] LoadMobSpawnDefinitions(string path, int currentZoneId)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<MobSpawnDefinitionData>();
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<MobSpawnDefinitionData>();
            }

            var file = JsonUtility.FromJson<MobSpawnDefinitionFileData>(json);
            if (file == null || file.mobSpawns == null)
            {
                return Array.Empty<MobSpawnDefinitionData>();
            }

            var filtered = new List<MobSpawnDefinitionData>();
            for (var i = 0; i < file.mobSpawns.Length; i++)
            {
                var spawn = file.mobSpawns[i];
                if (spawn != null && spawn.zoneId == currentZoneId)
                {
                    filtered.Add(spawn);
                }
            }

            return filtered.ToArray();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to load mob spawn definitions file: " + ex.Message);
            return Array.Empty<MobSpawnDefinitionData>();
        }
    }

    [Serializable]
    private sealed class NpcDefinitionFileData
    {
        public NpcDefinitionData[] npcs;
    }

    [Serializable]
    private sealed class MobSpawnDefinitionFileData
    {
        public MobSpawnDefinitionData[] mobSpawns;
    }

    [Serializable]
    public sealed class NpcDefinitionData
    {
        public string npcId;
        public int zoneId;
        public string npcTypeId;
        public string displayName;
        public float positionX;
        public float positionY;
        public float positionZ;
        public string primaryRole;
        public string[] services;
        public string greetingText;
        public NpcServiceDefinitionData[] serviceOptions;
    }

    [Serializable]
    public sealed class NpcServiceDefinitionData
    {
        public string actionId;
        public string label;
        public string uiHint;
    }

    [Serializable]
    public sealed class MobSpawnDefinitionData
    {
        public string spawnId;
        public int zoneId;
        public string mobTypeId;
        public float positionX;
        public float positionY;
        public float positionZ;
        public int count;
        public float radius;
        public float roamRadius;
    }
}
}
