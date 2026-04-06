using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorServerClient
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<LiveDashboardSnapshot> GetDashboardAsync(string baseUrl)
    {
        var json = await GetStringAsync(NormalizeBaseUrl(baseUrl) + "/api/dashboard");
        return JsonUtilityWrapper.FromJson<LiveDashboardSnapshot>(json);
    }

    public static async Task<LiveGameplayDefinitionsSnapshot> GetGameplayDefinitionsAsync(string baseUrl)
    {
        var json = await GetStringAsync(NormalizeBaseUrl(baseUrl) + "/api/gameplay-definitions");
        return JsonUtilityWrapper.FromJson<LiveGameplayDefinitionsSnapshot>(json);
    }

    public static async Task<LiveGameplayDefinitionsSnapshot> PutGameplayDefinitionsAsync(string baseUrl, LiveGameplayDefinitionsSnapshot snapshot)
    {
        var url = NormalizeBaseUrl(baseUrl) + "/api/gameplay-definitions";
        var json = JsonUtilityWrapper.ToJson(snapshot, true);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Http.PutAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }

        return JsonUtilityWrapper.FromJson<LiveGameplayDefinitionsSnapshot>(body);
    }

    public static async Task UploadZoneBundleAsync(
        string assetApiBaseUrl,
        string bundleFilePath,
        string bundleName,
        string platform,
        string unityVersion,
        string notes,
        string channel,
        string writeApiKey)
    {
        if (!File.Exists(bundleFilePath))
        {
            throw new FileNotFoundException("Bundle file was not found.", bundleFilePath);
        }

        var normalizedAssetApiBaseUrl = NormalizeBaseUrl(assetApiBaseUrl, "http://127.0.0.1:7095");
        var normalizedBundleName = string.IsNullOrWhiteSpace(bundleName) ? Path.GetFileName(bundleFilePath) : bundleName.Trim();
        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? "windows" : platform.Trim().ToLowerInvariant();
        var normalizedUnityVersion = string.IsNullOrWhiteSpace(unityVersion) ? "unknown" : unityVersion.Trim();
        var normalizedChannel = string.IsNullOrWhiteSpace(channel) ? "live" : channel.Trim().ToLowerInvariant();
        var version = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss");

        var artifact = await UploadArtifactAsync(normalizedAssetApiBaseUrl, bundleFilePath, writeApiKey);
        var bundleVersionId = await CreateBundleVersionAsync(
            normalizedAssetApiBaseUrl,
            normalizedBundleName,
            artifact.Hash,
            normalizedPlatform,
            normalizedUnityVersion,
            version,
            notes,
            writeApiKey);

        var currentManifest = await TryGetChannelManifestAsync(normalizedAssetApiBaseUrl, normalizedChannel);
        var manifestId = await CreateManifestAsync(normalizedAssetApiBaseUrl, normalizedChannel, "world-editor", writeApiKey);

        if (currentManifest?.entries != null)
        {
            for (var i = 0; i < currentManifest.entries.Length; i++)
            {
                var entry = currentManifest.entries[i];
                if (entry == null || entry.bundleVersionId <= 0)
                {
                    continue;
                }

                if (string.Equals(entry.bundleName, normalizedBundleName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.platform, normalizedPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await UpsertManifestEntryAsync(
                    normalizedAssetApiBaseUrl,
                    manifestId,
                    entry.bundleName,
                    entry.platform,
                    entry.bundleVersionId,
                    writeApiKey);
            }
        }

        await UpsertManifestEntryAsync(
            normalizedAssetApiBaseUrl,
            manifestId,
            normalizedBundleName,
            normalizedPlatform,
            bundleVersionId,
            writeApiKey);

        await PromoteChannelAsync(normalizedAssetApiBaseUrl, normalizedChannel, manifestId, "world-editor", writeApiKey);
    }

    private static async Task<string> GetStringAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }

        return body;
    }

    private static string ExtractError(string body, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Server request failed." : fallback;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        return client;
    }

    public static string NormalizeBaseUrl(string baseUrl)
        => NormalizeBaseUrl(baseUrl, "http://127.0.0.1:7080");

    public static string NormalizeBaseUrl(string baseUrl, string fallback)
        => (string.IsNullOrWhiteSpace(baseUrl) ? fallback : baseUrl.Trim()).TrimEnd('/');

    private static async Task<AssetArtifactResponse> UploadArtifactAsync(string assetApiBaseUrl, string bundleFilePath, string writeApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, assetApiBaseUrl + "/api/assets/artifacts?sourceName=" + Uri.EscapeDataString(Path.GetFileName(bundleFilePath)));
        ApplyWriteApiKey(request, writeApiKey);
        using var fileStream = File.OpenRead(bundleFilePath);
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }

        return ParseArtifactResponse(body);
    }

    private static async Task<long> CreateBundleVersionAsync(string assetApiBaseUrl, string bundleName, string artifactHash, string platform, string unityVersion, string version, string notes, string writeApiKey)
    {
        var payload =
            "{"
            + "\"version\":\"" + EscapeJson(version) + "\","
            + "\"artifactHash\":\"" + EscapeJson(artifactHash) + "\","
            + "\"platform\":\"" + EscapeJson(platform) + "\","
            + "\"unityVersion\":\"" + EscapeJson(unityVersion) + "\","
            + "\"assetType\":\"zone\","
            + "\"notes\":\"" + EscapeJson(notes ?? string.Empty) + "\""
            + "}";

        using var request = new HttpRequestMessage(HttpMethod.Post, assetApiBaseUrl + "/api/assets/bundles/" + Uri.EscapeDataString(bundleName) + "/versions");
        ApplyWriteApiKey(request, writeApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }

        return ParseInt64Property(body, "bundleVersionId");
    }

    private static async Task<AssetChannelManifestSnapshot> TryGetChannelManifestAsync(string assetApiBaseUrl, string channel)
    {
        using var response = await Http.GetAsync(assetApiBaseUrl + "/api/assets/channels/" + Uri.EscapeDataString(channel) + "/manifest");
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }

        return JsonUtilityWrapper.FromJson<AssetChannelManifestSnapshot>(body);
    }

    private static async Task<long> CreateManifestAsync(string assetApiBaseUrl, string channel, string createdBy, string writeApiKey)
    {
        var payload =
            "{"
            + "\"channel\":\"" + EscapeJson(channel) + "\","
            + "\"createdBy\":\"" + EscapeJson(createdBy) + "\""
            + "}";

        using var request = new HttpRequestMessage(HttpMethod.Post, assetApiBaseUrl + "/api/assets/manifests");
        ApplyWriteApiKey(request, writeApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }

        return ParseInt64Property(body, "manifestId");
    }

    private static async Task UpsertManifestEntryAsync(string assetApiBaseUrl, long manifestId, string bundleName, string platform, long bundleVersionId, string writeApiKey)
    {
        var payload =
            "{"
            + "\"bundleName\":\"" + EscapeJson(bundleName) + "\","
            + "\"platform\":\"" + EscapeJson(platform) + "\","
            + "\"bundleVersionId\":" + bundleVersionId
            + "}";

        using var request = new HttpRequestMessage(HttpMethod.Post, assetApiBaseUrl + "/api/assets/manifests/" + manifestId + "/entries");
        ApplyWriteApiKey(request, writeApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }
    }

    private static async Task PromoteChannelAsync(string assetApiBaseUrl, string channel, long manifestId, string actor, string writeApiKey)
    {
        var payload =
            "{"
            + "\"manifestId\":" + manifestId + ","
            + "\"actor\":\"" + EscapeJson(actor) + "\""
            + "}";

        using var request = new HttpRequestMessage(HttpMethod.Post, assetApiBaseUrl + "/api/assets/channels/" + Uri.EscapeDataString(channel) + "/promote");
        ApplyWriteApiKey(request, writeApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body, response.ReasonPhrase));
        }
    }

    private static void ApplyWriteApiKey(HttpRequestMessage request, string writeApiKey)
    {
        if (!string.IsNullOrWhiteSpace(writeApiKey))
        {
            request.Headers.Add("X-Asset-Api-Key", writeApiKey.Trim());
        }
    }

    private static AssetArtifactResponse ParseArtifactResponse(string json)
    {
        return new AssetArtifactResponse(
            ParseStringProperty(json, "hash"),
            ParseStringProperty(json, "sourceName"));
    }

    private static long ParseInt64Property(string json, string propertyName)
    {
        var match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(\\d+)");
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var value))
        {
            throw new InvalidOperationException("Response did not contain " + propertyName + ".");
        }

        return value;
    }

    private static string ParseStringProperty(string json, string propertyName)
    {
        var match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        if (!match.Success)
        {
            throw new InvalidOperationException("Response did not contain " + propertyName + ".");
        }

        return Regex.Unescape(match.Groups[1].Value);
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    private readonly struct AssetArtifactResponse
    {
        public AssetArtifactResponse(string hash, string sourceName)
        {
            Hash = hash;
            SourceName = sourceName;
        }

        public string Hash { get; }
        public string SourceName { get; }
    }
}
}
