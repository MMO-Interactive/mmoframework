using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var workspaceRoot = ResolveWorkspaceRoot();
var dataRoot = Environment.GetEnvironmentVariable("MMO_ASSET_DATA_ROOT")
    ?? Path.Combine(workspaceRoot, "Documents", "AssetServerData");
Directory.CreateDirectory(dataRoot);
Directory.CreateDirectory(Path.Combine(dataRoot, "artifacts"));

var databasePath = Environment.GetEnvironmentVariable("MMO_ASSET_DB")
    ?? Path.Combine(dataRoot, "AssetServer.db");

var store = new AssetMetadataStore(databasePath);
store.Initialize();
var maxUploadBytes = long.TryParse(Environment.GetEnvironmentVariable("MMO_ASSET_MAX_UPLOAD_BYTES"), out var parsedMaxUploadBytes)
    ? Math.Max(parsedMaxUploadBytes, 1L)
    : 512L * 1024L * 1024L;
var writeApiKey = Environment.GetEnvironmentVariable("MMO_ASSET_WRITE_API_KEY");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/assets/artifacts", async (HttpContext context, CancellationToken cancellationToken) =>
{
    var writeAuth = RequireWriteAuthorization(context, writeApiKey);
    if (writeAuth is not null)
    {
        return writeAuth;
    }

    var stream = context.Request.Body;
    if (stream is null)
    {
        return Results.BadRequest(new { error = "Request body is required." });
    }

    var sourceName = context.Request.Query["sourceName"].ToString();
    if (string.IsNullOrWhiteSpace(sourceName))
    {
        sourceName = "artifact.bin";
    }
    var expectedHash = context.Request.Query["expectedHash"].ToString().Trim().ToLowerInvariant();

    var tempPath = Path.Combine(dataRoot, $"upload-{Guid.NewGuid():N}.tmp");
    await using var destination = File.Create(tempPath);
    using var sha = SHA256.Create();

    var buffer = new byte[128 * 1024];
    long totalBytes = 0;
    while (true)
    {
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read <= 0)
        {
            break;
        }

        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        sha.TransformBlock(buffer, 0, read, null, 0);
        totalBytes += read;
        if (totalBytes > maxUploadBytes)
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Close();
            File.Delete(tempPath);
            return Results.BadRequest(new { error = $"Upload exceeded max size limit of {maxUploadBytes} bytes." });
        }
    }

    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

    var hash = Convert.ToHexStringLower(sha.Hash ?? Array.Empty<byte>());
    if (string.IsNullOrWhiteSpace(hash))
    {
        File.Delete(tempPath);
        return Results.BadRequest(new { error = "Could not compute content hash." });
    }
    if (!string.IsNullOrWhiteSpace(expectedHash) && !string.Equals(expectedHash, hash, StringComparison.Ordinal))
    {
        File.Delete(tempPath);
        return Results.BadRequest(new { error = $"Hash mismatch. expected={expectedHash} actual={hash}" });
    }

    var artifactPath = Path.Combine(dataRoot, "artifacts", hash);
    if (File.Exists(artifactPath))
    {
        File.Delete(tempPath);
    }
    else
    {
        File.Move(tempPath, artifactPath);
    }

    var artifact = store.UpsertArtifact(hash, sourceName, totalBytes, "application/octet-stream");
    return Results.Ok(artifact);
});

app.MapGet("/api/assets/artifacts/{hash}", (string hash) =>
{
    var normalized = hash.Trim().ToLowerInvariant();
    var path = Path.Combine(dataRoot, "artifacts", normalized);
    if (!File.Exists(path))
    {
        return Results.NotFound(new { error = "Artifact not found." });
    }

    return Results.File(path, "application/octet-stream", enableRangeProcessing: true);
});

app.MapGet("/api/assets/artifacts/{hash}/meta", (string hash) =>
{
    var artifact = store.GetArtifact(hash);
    return artifact is null ? Results.NotFound(new { error = "Artifact not found." }) : Results.Ok(artifact);
});

app.MapPost("/api/assets/bundles/{bundleName}/versions", (HttpContext context, string bundleName, BundleVersionCreateRequest request) =>
{
    var writeAuth = RequireWriteAuthorization(context, writeApiKey);
    if (writeAuth is not null)
    {
        return writeAuth;
    }

    if (string.IsNullOrWhiteSpace(request.ArtifactHash))
    {
        return Results.BadRequest(new { error = "artifactHash is required." });
    }

    var artifact = store.GetArtifact(request.ArtifactHash);
    if (artifact is null)
    {
        return Results.BadRequest(new { error = "Artifact hash was not found." });
    }

    var created = store.CreateBundleVersion(bundleName, request);
    return Results.Ok(created);
});

app.MapGet("/api/assets/bundles", (string? assetType) =>
{
    return Results.Ok(store.ListBundles(assetType));
});

app.MapPost("/api/assets/manifests", (HttpContext context, ManifestCreateRequest request) =>
{
    var writeAuth = RequireWriteAuthorization(context, writeApiKey);
    if (writeAuth is not null)
    {
        return writeAuth;
    }

    var created = store.CreateManifest(request.Channel, request.CreatedBy);
    return Results.Ok(created);
});

app.MapPost("/api/assets/manifests/{manifestId:long}/entries", (HttpContext context, long manifestId, ManifestEntryUpsertRequest request) =>
{
    var writeAuth = RequireWriteAuthorization(context, writeApiKey);
    if (writeAuth is not null)
    {
        return writeAuth;
    }

    if (string.IsNullOrWhiteSpace(request.BundleName) || string.IsNullOrWhiteSpace(request.Platform))
    {
        return Results.BadRequest(new { error = "bundleName and platform are required." });
    }

    var upserted = store.UpsertManifestEntry(manifestId, request);
    return upserted is null
        ? Results.BadRequest(new { error = "Manifest or bundle version was not found." })
        : Results.Ok(upserted);
});

app.MapPost("/api/assets/channels/{channel}/promote", (HttpContext context, string channel, ChannelPromotionRequest request) =>
{
    var writeAuth = RequireWriteAuthorization(context, writeApiKey);
    if (writeAuth is not null)
    {
        return writeAuth;
    }

    var promoted = store.PromoteChannel(channel, request.ManifestId, request.Actor);
    return promoted
        ? Results.Ok(new { channel, manifestId = request.ManifestId, promotedAtUtc = DateTime.UtcNow })
        : Results.BadRequest(new { error = "Manifest does not exist for promotion." });
});

app.MapGet("/api/assets/channels/{channel}/manifest", (string channel, string? platform) =>
{
    var manifest = store.GetResolvedManifest(channel, platform);
    return manifest is null ? Results.NotFound(new { error = "Channel manifest not found." }) : Results.Ok(manifest);
});

app.MapGet("/api/assets/channels", () => Results.Ok(store.ListChannelHeads()));
app.MapGet("/api/assets/channels/{channel}/history", (string channel) => Results.Ok(store.ListChannelHistory(channel)));
app.MapGet("/api/assets/manifests/{manifestId:long}", (long manifestId) =>
{
    var manifest = store.GetManifestById(manifestId);
    return manifest is null ? Results.NotFound(new { error = "Manifest not found." }) : Results.Ok(manifest);
});
app.MapGet("/api/assets/stats", () => Results.Ok(store.GetStats()));

var port = int.TryParse(Environment.GetEnvironmentVariable("MMO_ASSET_HTTP_PORT"), out var parsedPort) ? parsedPort : 7095;
app.Urls.Add($"http://0.0.0.0:{port}");
Console.WriteLine($"MMO Asset Server listening on http://127.0.0.1:{port}");
Console.WriteLine($"Asset data root: {dataRoot}");
Console.WriteLine(string.IsNullOrWhiteSpace(writeApiKey)
    ? "Asset write auth: disabled (MMO_ASSET_WRITE_API_KEY unset)"
    : "Asset write auth: enabled");
await app.RunAsync().ConfigureAwait(false);

static IResult? RequireWriteAuthorization(HttpContext context, string? configuredApiKey)
{
    if (string.IsNullOrWhiteSpace(configuredApiKey))
    {
        return null;
    }

    var provided = context.Request.Headers["X-Asset-Api-Key"].ToString();
    if (string.Equals(provided, configuredApiKey, StringComparison.Ordinal))
    {
        return null;
    }

    return Results.Unauthorized();
}

static string ResolveWorkspaceRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var hasShared = Directory.Exists(Path.Combine(current.FullName, "Shared", "MMONetworking"));
        var hasServer = Directory.Exists(Path.Combine(current.FullName, "Server", "MMONetworking.ServerHost"));
        var hasClient = Directory.Exists(Path.Combine(current.FullName, "Client"));
        if (hasShared && hasServer && hasClient)
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

internal sealed record ArtifactRecord(string Hash, string SourceName, long SizeBytes, string ContentType, DateTime CreatedAtUtc);
internal sealed record BundleVersionCreateRequest(string Version, string ArtifactHash, string Platform, string UnityVersion, string? AssetType, string? Notes);
internal sealed record BundleVersionRecord(long BundleVersionId, string BundleName, string Version, string ArtifactHash, string Platform, string UnityVersion, string AssetType, string? Notes, DateTime CreatedAtUtc);
internal sealed record ManifestCreateRequest(string Channel, string CreatedBy);
internal sealed record ManifestRecord(long ManifestId, string Channel, string CreatedBy, DateTime CreatedAtUtc);
internal sealed record ManifestEntryUpsertRequest(string BundleName, string Platform, long BundleVersionId);
internal sealed record ManifestEntryRecord(long ManifestId, string BundleName, string Platform, long BundleVersionId);
internal sealed record ChannelPromotionRequest(long ManifestId, string Actor);

internal sealed class AssetMetadataStore
{
    private readonly string _connectionString;

    public AssetMetadataStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS artifacts (
  hash TEXT PRIMARY KEY,
  source_name TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  content_type TEXT NOT NULL,
  created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS bundle_versions (
  bundle_version_id INTEGER PRIMARY KEY AUTOINCREMENT,
  bundle_name TEXT NOT NULL,
  version TEXT NOT NULL,
  artifact_hash TEXT NOT NULL,
  platform TEXT NOT NULL,
  unity_version TEXT NOT NULL,
  asset_type TEXT NOT NULL DEFAULT 'generic',
  notes TEXT NULL,
  created_at_utc TEXT NOT NULL,
  UNIQUE(bundle_name, version, platform),
  FOREIGN KEY(artifact_hash) REFERENCES artifacts(hash)
);

CREATE TABLE IF NOT EXISTS manifests (
  manifest_id INTEGER PRIMARY KEY AUTOINCREMENT,
  channel TEXT NOT NULL,
  created_by TEXT NOT NULL,
  created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS manifest_entries (
  manifest_id INTEGER NOT NULL,
  bundle_name TEXT NOT NULL,
  platform TEXT NOT NULL,
  bundle_version_id INTEGER NOT NULL,
  PRIMARY KEY (manifest_id, bundle_name, platform),
  FOREIGN KEY(manifest_id) REFERENCES manifests(manifest_id),
  FOREIGN KEY(bundle_version_id) REFERENCES bundle_versions(bundle_version_id)
);

CREATE TABLE IF NOT EXISTS channel_heads (
  channel TEXT PRIMARY KEY,
  manifest_id INTEGER NOT NULL,
  actor TEXT NOT NULL,
  promoted_at_utc TEXT NOT NULL,
  FOREIGN KEY(manifest_id) REFERENCES manifests(manifest_id)
);

CREATE TABLE IF NOT EXISTS channel_history (
  channel_history_id INTEGER PRIMARY KEY AUTOINCREMENT,
  channel TEXT NOT NULL,
  manifest_id INTEGER NOT NULL,
  actor TEXT NOT NULL,
  promoted_at_utc TEXT NOT NULL,
  FOREIGN KEY(manifest_id) REFERENCES manifests(manifest_id)
);";
        command.ExecuteNonQuery();
        EnsureBundleVersionAssetTypeColumn(connection);
    }

    public ArtifactRecord UpsertArtifact(string hash, string sourceName, long sizeBytes, string contentType)
    {
        var now = DateTime.UtcNow;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO artifacts (hash, source_name, size_bytes, content_type, created_at_utc)
VALUES ($hash, $source, $size, $contentType, $created)
ON CONFLICT(hash) DO UPDATE SET source_name = excluded.source_name;";
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$source", sourceName);
        command.Parameters.AddWithValue("$size", sizeBytes);
        command.Parameters.AddWithValue("$contentType", contentType);
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.ExecuteNonQuery();
        return new ArtifactRecord(hash, sourceName, sizeBytes, contentType, now);
    }

    public ArtifactRecord? GetArtifact(string hash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hash, source_name, size_bytes, content_type, created_at_utc FROM artifacts WHERE hash = $hash LIMIT 1;";
        command.Parameters.AddWithValue("$hash", hash.Trim().ToLowerInvariant());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ArtifactRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            DateTime.Parse(reader.GetString(4)));
    }

    public BundleVersionRecord CreateBundleVersion(string bundleName, BundleVersionCreateRequest request)
    {
        var now = DateTime.UtcNow;
        using var connection = Open();

        using (var existing = connection.CreateCommand())
        {
            existing.CommandText = @"
SELECT bundle_version_id, bundle_name, version, artifact_hash, platform, unity_version, asset_type, notes, created_at_utc
FROM bundle_versions
WHERE bundle_name = $bundle AND version = $version AND platform = $platform
LIMIT 1;";
            existing.Parameters.AddWithValue("$bundle", bundleName.Trim());
            existing.Parameters.AddWithValue("$version", request.Version.Trim());
            existing.Parameters.AddWithValue("$platform", request.Platform.Trim().ToLowerInvariant());
            using var reader = existing.ExecuteReader();
            if (reader.Read())
            {
                return new BundleVersionRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    DateTime.Parse(reader.GetString(8)));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO bundle_versions (bundle_name, version, artifact_hash, platform, unity_version, asset_type, notes, created_at_utc)
VALUES ($bundle, $version, $artifact, $platform, $unity, $assetType, $notes, $created);
SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$bundle", bundleName.Trim());
        command.Parameters.AddWithValue("$version", request.Version.Trim());
        command.Parameters.AddWithValue("$artifact", request.ArtifactHash.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$platform", request.Platform.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$unity", request.UnityVersion.Trim());
        command.Parameters.AddWithValue("$assetType", string.IsNullOrWhiteSpace(request.AssetType) ? "generic" : request.AssetType.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());
        command.Parameters.AddWithValue("$created", now.ToString("O"));

        var id = (long)(command.ExecuteScalar() ?? 0L);
        return new BundleVersionRecord(id, bundleName.Trim(), request.Version.Trim(), request.ArtifactHash.Trim().ToLowerInvariant(), request.Platform.Trim().ToLowerInvariant(), request.UnityVersion.Trim(), string.IsNullOrWhiteSpace(request.AssetType) ? "generic" : request.AssetType.Trim().ToLowerInvariant(), request.Notes, now);
    }

    public IReadOnlyList<object> ListBundles(string? assetType)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT bundle_name, version, platform, unity_version, asset_type, artifact_hash, created_at_utc
FROM bundle_versions
WHERE ($assetType IS NULL OR asset_type = $assetType)
ORDER BY created_at_utc DESC, bundle_name ASC;";
        command.Parameters.AddWithValue("$assetType", string.IsNullOrWhiteSpace(assetType) ? DBNull.Value : assetType.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        while (reader.Read())
        {
            rows.Add(new
            {
                bundleName = reader.GetString(0),
                version = reader.GetString(1),
                platform = reader.GetString(2),
                unityVersion = reader.GetString(3),
                assetType = reader.GetString(4),
                artifactHash = reader.GetString(5),
                createdAtUtc = reader.GetString(6)
            });
        }

        return rows;
    }

    public IReadOnlyList<object> ListChannelHeads()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT c.channel, c.manifest_id, c.actor, c.promoted_at_utc, m.created_at_utc
FROM channel_heads c
JOIN manifests m ON m.manifest_id = c.manifest_id
ORDER BY c.channel ASC;";
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        while (reader.Read())
        {
            rows.Add(new
            {
                channel = reader.GetString(0),
                manifestId = reader.GetInt64(1),
                actor = reader.GetString(2),
                promotedAtUtc = reader.GetString(3),
                manifestCreatedAtUtc = reader.GetString(4)
            });
        }

        return rows;
    }

    public IReadOnlyList<object> ListChannelHistory(string channel)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT channel, manifest_id, actor, promoted_at_utc
FROM channel_history
WHERE channel = $channel
ORDER BY promoted_at_utc DESC;";
        command.Parameters.AddWithValue("$channel", channel.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        while (reader.Read())
        {
            rows.Add(new
            {
                channel = reader.GetString(0),
                manifestId = reader.GetInt64(1),
                actor = reader.GetString(2),
                promotedAtUtc = reader.GetString(3)
            });
        }

        return rows;
    }

    public object? GetManifestById(long manifestId)
    {
        using var connection = Open();
        string? channel = null;
        string? createdBy = null;
        string? createdAt = null;
        using (var manifest = connection.CreateCommand())
        {
            manifest.CommandText = "SELECT channel, created_by, created_at_utc FROM manifests WHERE manifest_id = $id LIMIT 1;";
            manifest.Parameters.AddWithValue("$id", manifestId);
            using var reader = manifest.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            channel = reader.GetString(0);
            createdBy = reader.GetString(1);
            createdAt = reader.GetString(2);
        }

        var entries = new List<object>();
        using (var entriesCommand = connection.CreateCommand())
        {
            entriesCommand.CommandText = @"
SELECT e.bundle_name, e.platform, b.version, b.asset_type, b.artifact_hash, b.unity_version
FROM manifest_entries e
JOIN bundle_versions b ON b.bundle_version_id = e.bundle_version_id
WHERE e.manifest_id = $id
ORDER BY e.bundle_name ASC;";
            entriesCommand.Parameters.AddWithValue("$id", manifestId);
            using var reader = entriesCommand.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new
                {
                    bundleName = reader.GetString(0),
                    platform = reader.GetString(1),
                    version = reader.GetString(2),
                    assetType = reader.GetString(3),
                    artifactHash = reader.GetString(4),
                    unityVersion = reader.GetString(5)
                });
            }
        }

        return new
        {
            manifestId,
            channel,
            createdBy,
            createdAtUtc = createdAt,
            entries
        };
    }

    public object GetStats()
    {
        using var connection = Open();
        long artifactCount = 0;
        long artifactBytes = 0;
        long bundleVersionCount = 0;
        long manifestCount = 0;
        long channelCount = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(1), COALESCE(SUM(size_bytes), 0) FROM artifacts;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                artifactCount = reader.GetInt64(0);
                artifactBytes = reader.GetInt64(1);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(1) FROM bundle_versions;";
            bundleVersionCount = (long)(command.ExecuteScalar() ?? 0L);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(1) FROM manifests;";
            manifestCount = (long)(command.ExecuteScalar() ?? 0L);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(1) FROM channel_heads;";
            channelCount = (long)(command.ExecuteScalar() ?? 0L);
        }

        return new
        {
            generatedAtUtc = DateTime.UtcNow,
            artifacts = new { count = artifactCount, totalBytes = artifactBytes },
            bundleVersions = bundleVersionCount,
            manifests = manifestCount,
            channels = channelCount
        };
    }

    public ManifestRecord CreateManifest(string channel, string createdBy)
    {
        var now = DateTime.UtcNow;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO manifests (channel, created_by, created_at_utc)
VALUES ($channel, $createdBy, $created);
SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$channel", channel.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$createdBy", string.IsNullOrWhiteSpace(createdBy) ? "unknown" : createdBy.Trim());
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        var id = (long)(command.ExecuteScalar() ?? 0L);
        return new ManifestRecord(id, channel.Trim().ToLowerInvariant(), createdBy, now);
    }

    public ManifestEntryRecord? UpsertManifestEntry(long manifestId, ManifestEntryUpsertRequest request)
    {
        using var connection = Open();

        using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = "SELECT COUNT(1) FROM manifests WHERE manifest_id = $id;";
            existsCommand.Parameters.AddWithValue("$id", manifestId);
            if ((long)(existsCommand.ExecuteScalar() ?? 0L) == 0)
            {
                return null;
            }
        }

        using (var bundleExistsCommand = connection.CreateCommand())
        {
            bundleExistsCommand.CommandText = "SELECT COUNT(1) FROM bundle_versions WHERE bundle_version_id = $id;";
            bundleExistsCommand.Parameters.AddWithValue("$id", request.BundleVersionId);
            if ((long)(bundleExistsCommand.ExecuteScalar() ?? 0L) == 0)
            {
                return null;
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO manifest_entries (manifest_id, bundle_name, platform, bundle_version_id)
VALUES ($manifest, $bundle, $platform, $versionId)
ON CONFLICT(manifest_id, bundle_name, platform)
DO UPDATE SET bundle_version_id = excluded.bundle_version_id;";
        command.Parameters.AddWithValue("$manifest", manifestId);
        command.Parameters.AddWithValue("$bundle", request.BundleName.Trim());
        command.Parameters.AddWithValue("$platform", request.Platform.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$versionId", request.BundleVersionId);
        command.ExecuteNonQuery();

        return new ManifestEntryRecord(manifestId, request.BundleName.Trim(), request.Platform.Trim().ToLowerInvariant(), request.BundleVersionId);
    }

    public bool PromoteChannel(string channel, long manifestId, string actor)
    {
        using var connection = Open();
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(1) FROM manifests WHERE manifest_id = $id;";
            exists.Parameters.AddWithValue("$id", manifestId);
            if ((long)(exists.ExecuteScalar() ?? 0L) == 0)
            {
                return false;
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO channel_heads (channel, manifest_id, actor, promoted_at_utc)
VALUES ($channel, $manifest, $actor, $promoted)
ON CONFLICT(channel)
DO UPDATE SET manifest_id = excluded.manifest_id, actor = excluded.actor, promoted_at_utc = excluded.promoted_at_utc;";
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();
        var promotedAt = DateTime.UtcNow.ToString("O");
        command.Parameters.AddWithValue("$channel", channel.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$manifest", manifestId);
        command.Parameters.AddWithValue("$actor", normalizedActor);
        command.Parameters.AddWithValue("$promoted", promotedAt);
        command.ExecuteNonQuery();

        using var history = connection.CreateCommand();
        history.CommandText = @"
INSERT INTO channel_history (channel, manifest_id, actor, promoted_at_utc)
VALUES ($channel, $manifest, $actor, $promoted);";
        history.Parameters.AddWithValue("$channel", channel.Trim().ToLowerInvariant());
        history.Parameters.AddWithValue("$manifest", manifestId);
        history.Parameters.AddWithValue("$actor", normalizedActor);
        history.Parameters.AddWithValue("$promoted", promotedAt);
        history.ExecuteNonQuery();
        return true;
    }

    public object? GetResolvedManifest(string channel, string? platform)
    {
        using var connection = Open();

        long manifestId;
        using (var head = connection.CreateCommand())
        {
            head.CommandText = "SELECT manifest_id FROM channel_heads WHERE channel = $channel LIMIT 1;";
            head.Parameters.AddWithValue("$channel", channel.Trim().ToLowerInvariant());
            var result = head.ExecuteScalar();
            if (result is null || result is DBNull)
            {
                return null;
            }

            manifestId = (long)result;
        }

        var entries = new List<object>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT e.bundle_name, e.platform, b.version, b.artifact_hash, b.unity_version
FROM manifest_entries e
JOIN bundle_versions b ON b.bundle_version_id = e.bundle_version_id
WHERE e.manifest_id = $manifest
  AND ($platform IS NULL OR e.platform = $platform)
ORDER BY e.bundle_name ASC;";
        command.Parameters.AddWithValue("$manifest", manifestId);
        command.Parameters.AddWithValue("$platform", string.IsNullOrWhiteSpace(platform) ? DBNull.Value : platform.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new
            {
                bundleName = reader.GetString(0),
                platform = reader.GetString(1),
                version = reader.GetString(2),
                artifactHash = reader.GetString(3),
                unityVersion = reader.GetString(4),
                downloadUrl = $"/api/assets/artifacts/{reader.GetString(3)}"
            });
        }

        return new
        {
            channel = channel.Trim().ToLowerInvariant(),
            manifestId,
            generatedAtUtc = DateTime.UtcNow,
            entries
        };
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void EnsureBundleVersionAssetTypeColumn(SqliteConnection connection)
    {
        using var tableInfo = connection.CreateCommand();
        tableInfo.CommandText = "PRAGMA table_info(bundle_versions);";
        using var reader = tableInfo.ExecuteReader();
        var hasAssetType = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "asset_type", StringComparison.OrdinalIgnoreCase))
            {
                hasAssetType = true;
                break;
            }
        }

        if (hasAssetType)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE bundle_versions ADD COLUMN asset_type TEXT NOT NULL DEFAULT 'generic';";
        alter.ExecuteNonQuery();
    }
}
