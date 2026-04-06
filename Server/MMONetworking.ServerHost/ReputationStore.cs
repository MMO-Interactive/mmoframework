using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class ReputationStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public ReputationStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public ReputationProfileSnapshot GetProfile(ulong characterId, int actionLimit = 120)
    {
        if (characterId == 0)
        {
            throw new InvalidOperationException("characterId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return LoadProfile(connection, characterId, actionLimit);
        }
    }

    public ReputationProfileSnapshot Award(ulong characterId, string factionId, int amount, string reason, string actor)
    {
        if (characterId == 0)
        {
            throw new InvalidOperationException("characterId is required.");
        }

        if (string.IsNullOrWhiteSpace(factionId))
        {
            throw new InvalidOperationException("factionId is required.");
        }

        if (amount == 0)
        {
            throw new InvalidOperationException("amount cannot be zero.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    INSERT INTO reputation_state(character_id, faction_id, points, updated_at_utc)
                    VALUES ($character, $faction, $amount, $updated)
                    ON CONFLICT(character_id, faction_id) DO UPDATE SET
                        points = points + excluded.points,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                command.Parameters.AddWithValue("$character", unchecked((long)characterId));
                command.Parameters.AddWithValue("$faction", factionId.Trim());
                command.Parameters.AddWithValue("$amount", amount);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    INSERT INTO reputation_actions(character_id, faction_id, amount, reason, actor, created_at_utc)
                    VALUES ($character, $faction, $amount, $reason, $actor, $created);
                    """;
                command.Parameters.AddWithValue("$character", unchecked((long)characterId));
                command.Parameters.AddWithValue("$faction", factionId.Trim());
                command.Parameters.AddWithValue("$amount", amount);
                command.Parameters.AddWithValue("$reason", reason);
                command.Parameters.AddWithValue("$actor", actor);
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            tx.Commit();
            return LoadProfile(connection, characterId, 120);
        }
    }

    private static ReputationProfileSnapshot LoadProfile(SqliteConnection connection, ulong characterId, int actionLimit)
    {
        var factions = new List<FactionReputationSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT faction_id, points
                FROM reputation_state
                WHERE character_id = $character
                ORDER BY points DESC, faction_id ASC;
                """;
            command.Parameters.AddWithValue("$character", unchecked((long)characterId));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var points = reader.GetInt32(1);
                factions.Add(new FactionReputationSnapshot(
                    reader.GetString(0),
                    points,
                    ComputeTier(points)));
            }
        }

        var actions = new List<ReputationActionSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, faction_id, amount, reason, actor, created_at_utc
                FROM reputation_actions
                WHERE character_id = $character
                ORDER BY id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$character", unchecked((long)characterId));
            command.Parameters.AddWithValue("$limit", actionLimit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                actions.Add(new ReputationActionSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    DateTimeOffset.Parse(reader.GetString(5))));
            }
        }

        return new ReputationProfileSnapshot(characterId, factions.ToArray(), actions.ToArray());
    }

    private static string ComputeTier(int points)
        => points switch
        {
            >= 1200 => "Exalted",
            >= 700 => "Honored",
            >= 300 => "Friendly",
            > -100 => "Neutral",
            > -500 => "Unfriendly",
            _ => "Hostile"
        };

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        EnsureReputationStateSchema(connection);
        EnsureReputationActionsSchema(connection);
    }

    private static void EnsureReputationStateSchema(SqliteConnection connection)
    {
        if (!TableExists(connection, "reputation_state"))
        {
            CreateCharacterScopedReputationStateTable(connection, "reputation_state");
        }
        else if (ColumnExists(connection, "reputation_state", "account_id"))
        {
            MigrateLegacyReputationState(connection);
        }

        using var stateIndex = connection.CreateCommand();
        stateIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_reputation_state_character_faction ON reputation_state(character_id, faction_id);";
        stateIndex.ExecuteNonQuery();
    }

    private static void EnsureReputationActionsSchema(SqliteConnection connection)
    {
        if (!TableExists(connection, "reputation_actions"))
        {
            CreateCharacterScopedReputationActionsTable(connection, "reputation_actions");
        }
        else if (ColumnExists(connection, "reputation_actions", "account_id"))
        {
            MigrateLegacyReputationActions(connection);
        }
    }

    private static void MigrateLegacyReputationState(SqliteConnection connection)
    {
        CreateCharacterScopedReputationStateTable(connection, "reputation_state_v2");

        using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText =
                """
                INSERT OR REPLACE INTO reputation_state_v2(character_id, faction_id, points, updated_at_utc)
                SELECT resolved.character_id, resolved.faction_id, resolved.points, resolved.updated_at_utc
                FROM (
                    SELECT
                        CASE
                            WHEN reputation_state.character_id IS NOT NULL THEN reputation_state.character_id
                            ELSE (
                                SELECT primary_character_id
                                FROM accounts
                                WHERE accounts.account_id = reputation_state.account_id
                            )
                        END AS character_id,
                        reputation_state.faction_id,
                        reputation_state.points,
                        reputation_state.updated_at_utc
                    FROM reputation_state
                ) AS resolved
                WHERE resolved.character_id IS NOT NULL;
                """;
            migrate.ExecuteNonQuery();
        }

        using (var dropLegacy = connection.CreateCommand())
        {
            dropLegacy.CommandText = "DROP TABLE reputation_state;";
            dropLegacy.ExecuteNonQuery();
        }

        using (var rename = connection.CreateCommand())
        {
            rename.CommandText = "ALTER TABLE reputation_state_v2 RENAME TO reputation_state;";
            rename.ExecuteNonQuery();
        }
    }

    private static void MigrateLegacyReputationActions(SqliteConnection connection)
    {
        CreateCharacterScopedReputationActionsTable(connection, "reputation_actions_v2");

        using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText =
                """
                INSERT INTO reputation_actions_v2(id, character_id, faction_id, amount, reason, actor, created_at_utc)
                SELECT
                    reputation_actions.id,
                    CASE
                        WHEN reputation_actions.character_id IS NOT NULL THEN reputation_actions.character_id
                        ELSE (
                            SELECT primary_character_id
                            FROM accounts
                            WHERE accounts.account_id = reputation_actions.account_id
                        )
                    END,
                    reputation_actions.faction_id,
                    reputation_actions.amount,
                    reputation_actions.reason,
                    reputation_actions.actor,
                    reputation_actions.created_at_utc
                FROM reputation_actions
                WHERE CASE
                        WHEN reputation_actions.character_id IS NOT NULL THEN reputation_actions.character_id
                        ELSE (
                            SELECT primary_character_id
                            FROM accounts
                            WHERE accounts.account_id = reputation_actions.account_id
                        )
                      END IS NOT NULL;
                """;
            migrate.ExecuteNonQuery();
        }

        using (var dropLegacy = connection.CreateCommand())
        {
            dropLegacy.CommandText = "DROP TABLE reputation_actions;";
            dropLegacy.ExecuteNonQuery();
        }

        using (var rename = connection.CreateCommand())
        {
            rename.CommandText = "ALTER TABLE reputation_actions_v2 RENAME TO reputation_actions;";
            rename.ExecuteNonQuery();
        }
    }

    private static void CreateCharacterScopedReputationStateTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                character_id INTEGER NOT NULL,
                faction_id TEXT NOT NULL,
                points INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(character_id, faction_id)
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateCharacterScopedReputationActionsTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                character_id INTEGER NOT NULL,
                faction_id TEXT NOT NULL,
                amount INTEGER NOT NULL,
                reason TEXT NOT NULL,
                actor TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() != null;
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
