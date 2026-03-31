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

    public ReputationProfileSnapshot GetProfile(string accountId, int actionLimit = 120)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("accountId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return LoadProfile(connection, accountId.Trim(), actionLimit);
        }
    }

    public ReputationProfileSnapshot Award(string accountId, string factionId, int amount, string reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("accountId is required.");
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
                    INSERT INTO reputation_state(account_id, faction_id, points, updated_at_utc)
                    VALUES ($account, $faction, $amount, $updated)
                    ON CONFLICT(account_id, faction_id) DO UPDATE SET
                        points = points + excluded.points,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                command.Parameters.AddWithValue("$account", accountId.Trim());
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
                    INSERT INTO reputation_actions(account_id, faction_id, amount, reason, actor, created_at_utc)
                    VALUES ($account, $faction, $amount, $reason, $actor, $created);
                    """;
                command.Parameters.AddWithValue("$account", accountId.Trim());
                command.Parameters.AddWithValue("$faction", factionId.Trim());
                command.Parameters.AddWithValue("$amount", amount);
                command.Parameters.AddWithValue("$reason", reason);
                command.Parameters.AddWithValue("$actor", actor);
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            tx.Commit();
            return LoadProfile(connection, accountId.Trim(), 120);
        }
    }

    private static ReputationProfileSnapshot LoadProfile(SqliteConnection connection, string accountId, int actionLimit)
    {
        var factions = new List<FactionReputationSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT faction_id, points
                FROM reputation_state
                WHERE account_id = $account
                ORDER BY points DESC, faction_id ASC;
                """;
            command.Parameters.AddWithValue("$account", accountId);
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
                WHERE account_id = $account
                ORDER BY id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$account", accountId);
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

        return new ReputationProfileSnapshot(accountId, factions.ToArray(), actions.ToArray());
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
                    faction_id TEXT NOT NULL,
                    amount INTEGER NOT NULL,
                    reason TEXT NOT NULL,
                    actor TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }
}
