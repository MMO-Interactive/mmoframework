using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class CharacterProgressionStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public CharacterProgressionStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public CharacterProgressionSnapshot Get(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("accountId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT track_id, level, experience FROM character_progression WHERE account_id = $account ORDER BY track_id;";
            command.Parameters.AddWithValue("$account", accountId);
            using var reader = command.ExecuteReader();
            var tracks = new List<ProgressionTrackSnapshot>();
            while (reader.Read())
            {
                var level = reader.GetInt32(1);
                var xp = reader.GetInt32(2);
                tracks.Add(new ProgressionTrackSnapshot(reader.GetString(0), level, xp, ExperienceRequiredForLevel(level + 1)));
            }

            return new CharacterProgressionSnapshot(accountId, tracks.ToArray());
        }
    }

    public CharacterProgressionSnapshot GrantExperience(string accountId, string trackId, int xpDelta)
    {
        if (xpDelta <= 0)
        {
            throw new InvalidOperationException("xpDelta must be > 0.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            var (level, xp) = LoadTrack(connection, tx, accountId, trackId);
            xp += xpDelta;
            var nextLevelXp = ExperienceRequiredForLevel(level + 1);
            while (xp >= nextLevelXp)
            {
                xp -= nextLevelXp;
                level++;
                nextLevelXp = ExperienceRequiredForLevel(level + 1);
            }

            using var upsert = connection.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText =
                """
                INSERT INTO character_progression(account_id, track_id, level, experience, updated_at_utc)
                VALUES ($account, $track, $level, $xp, $updated)
                ON CONFLICT(account_id, track_id) DO UPDATE SET
                    level = excluded.level,
                    experience = excluded.experience,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsert.Parameters.AddWithValue("$account", accountId);
            upsert.Parameters.AddWithValue("$track", trackId);
            upsert.Parameters.AddWithValue("$level", level);
            upsert.Parameters.AddWithValue("$xp", xp);
            upsert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            upsert.ExecuteNonQuery();
            tx.Commit();
        }

        return Get(accountId);
    }

    private static (int Level, int Experience) LoadTrack(SqliteConnection connection, SqliteTransaction tx, string accountId, string trackId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT level, experience FROM character_progression WHERE account_id = $account AND track_id = $track LIMIT 1;";
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$track", trackId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (1, 0);
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static int ExperienceRequiredForLevel(int level)
    {
        var safeLevel = Math.Max(1, level);
        return 50 + (safeLevel * 25);
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS character_progression (
                account_id TEXT NOT NULL,
                track_id TEXT NOT NULL,
                level INTEGER NOT NULL,
                experience INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (account_id, track_id)
            );
            """;
        command.ExecuteNonQuery();
    }
}
