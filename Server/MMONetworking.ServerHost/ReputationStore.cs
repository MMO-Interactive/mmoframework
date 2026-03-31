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

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS reputation_state (
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    faction_id TEXT NOT NULL,
                    points INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY(account_id, faction_id)
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS reputation_actions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    faction_id TEXT NOT NULL,
                    amount INTEGER NOT NULL,
                    reason TEXT NOT NULL,
                    actor TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var migrateState = connection.CreateCommand())
        {
            migrateState.CommandText =
                """
                UPDATE reputation_state
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = reputation_state.account_id
                )
                WHERE character_id IS NULL;
                """;
            migrateState.ExecuteNonQuery();
        }

        using (var migrateActions = connection.CreateCommand())
        {
            migrateActions.CommandText =
                """
                UPDATE reputation_actions
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = reputation_actions.account_id
                )
                WHERE character_id IS NULL;
                """;
            migrateActions.ExecuteNonQuery();
        }

        using (var stateIndex = connection.CreateCommand())
        {
            stateIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_reputation_state_character_faction ON reputation_state(character_id, faction_id);";
            stateIndex.ExecuteNonQuery();
        }
    }
}
