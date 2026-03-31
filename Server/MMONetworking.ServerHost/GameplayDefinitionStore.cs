using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class GameplayDefinitionStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public GameplayDefinitionStore(string databasePath, ZoneDirectory zoneDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
        SeedDefaultsIfEmpty(zoneDirectory);
    }

    public GameplayDefinitionsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return ReadSnapshot(connection);
        }
    }

    public GameplayDefinitionsSnapshot Update(GameplayDefinitionsSnapshot next)
    {
        Validate(next);

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            UpsertSection(connection, transaction, "items", next.Items);
            UpsertSection(connection, transaction, "skills", next.Skills);
            UpsertSection(connection, transaction, "resources", next.Resources);
            UpsertSection(connection, transaction, "nodes", next.Nodes);
            UpsertSection(connection, transaction, "zones", next.Zones);
            transaction.Commit();
            return ReadSnapshot(connection);
        }
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS gameplay_definitions (
                section TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private void SeedDefaultsIfEmpty(ZoneDirectory zoneDirectory)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM gameplay_definitions;";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        var defaults = new GameplayDefinitionsSnapshot(
            Items: new[]
            {
                new ItemDefinitionSnapshot("log", "Wood Log", 200, 1.0f),
                new ItemDefinitionSnapshot("ore", "Iron Ore", 200, 1.5f)
            },
            Skills: new[]
            {
                new SkillDefinitionSnapshot("gathering", "Gathering", 100),
                new SkillDefinitionSnapshot("mining", "Mining", 100)
            },
            Resources: new[]
            {
                new ResourceDefinitionSnapshot("tree", "Tree", "log", 1),
                new ResourceDefinitionSnapshot("ore_vein", "Ore Vein", "ore", 1)
            },
            Nodes: new[]
            {
                new ResourceNodeDefinitionSnapshot("tree-1", 1, "tree", 10f, 0f, 45f, 8),
                new ResourceNodeDefinitionSnapshot("ore-1", 1, "ore_vein", 15f, 0f, 55f, 8)
            },
            Zones: zoneDirectory.All
                .OrderBy(zone => zone.ZoneId)
                .Select(zone => new ZoneDefinitionSnapshot(zone.ZoneId, zone.Name, zone.MinX, zone.MaxX, zone.MinZ, zone.MaxZ))
                .ToArray());

        using var transaction = connection.BeginTransaction();
        UpsertSection(connection, transaction, "items", defaults.Items);
        UpsertSection(connection, transaction, "skills", defaults.Skills);
        UpsertSection(connection, transaction, "resources", defaults.Resources);
        UpsertSection(connection, transaction, "nodes", defaults.Nodes);
        UpsertSection(connection, transaction, "zones", defaults.Zones);
        transaction.Commit();
    }

    private GameplayDefinitionsSnapshot ReadSnapshot(SqliteConnection connection)
        => new(
            Items: ReadSection<ItemDefinitionSnapshot>(connection, "items"),
            Skills: ReadSection<SkillDefinitionSnapshot>(connection, "skills"),
            Resources: ReadSection<ResourceDefinitionSnapshot>(connection, "resources"),
            Nodes: ReadSection<ResourceNodeDefinitionSnapshot>(connection, "nodes"),
            Zones: ReadSection<ZoneDefinitionSnapshot>(connection, "zones"));

    private static T[] ReadSection<T>(SqliteConnection connection, string section)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM gameplay_definitions WHERE section = $section LIMIT 1;";
        command.Parameters.AddWithValue("$section", section);
        var payload = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<T>();
        }

        return JsonSerializer.Deserialize<T[]>(payload, JsonOptions) ?? Array.Empty<T>();
    }

    private static void UpsertSection<T>(SqliteConnection connection, SqliteTransaction transaction, string section, IReadOnlyCollection<T> values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO gameplay_definitions(section, payload_json, updated_at_utc)
            VALUES ($section, $payload, $updated)
            ON CONFLICT(section) DO UPDATE SET
                payload_json = excluded.payload_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$section", section);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(values, JsonOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void Validate(GameplayDefinitionsSnapshot snapshot)
    {
        if (snapshot.Items.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate item ids are not allowed.");
        }

        if (snapshot.Skills.GroupBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate skill ids are not allowed.");
        }

        if (snapshot.Resources.Any(resource => snapshot.Items.All(item => !string.Equals(item.Id, resource.ItemId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Every resource definition must reference an existing item id.");
        }

        if (snapshot.Nodes.Any(node => snapshot.Resources.All(resource => !string.Equals(resource.Id, node.ResourceId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Every node definition must reference an existing resource id.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
