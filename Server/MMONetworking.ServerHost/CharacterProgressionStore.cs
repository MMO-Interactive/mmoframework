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

    public CharacterProgressionSnapshot Get(ulong characterId)
    {
        if (characterId == 0)
        {
            throw new InvalidOperationException("characterId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT track_id, level, experience FROM character_progression WHERE character_id = $character ORDER BY track_id;";
            command.Parameters.AddWithValue("$character", unchecked((long)characterId));
            using var reader = command.ExecuteReader();
            var tracks = new List<ProgressionTrackSnapshot>();
            while (reader.Read())
            {
                var level = reader.GetInt32(1);
                var xp = reader.GetInt32(2);
                tracks.Add(new ProgressionTrackSnapshot(reader.GetString(0), level, xp, ExperienceRequiredForLevel(level + 1)));
            }

            return new CharacterProgressionSnapshot(characterId, tracks.ToArray());
        }
    }

    public CharacterProgressionSnapshot GrantExperience(ulong characterId, string trackId, int xpDelta)
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

            var (level, xp) = LoadTrack(connection, tx, characterId, trackId);
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
                INSERT INTO character_progression(character_id, track_id, level, experience, updated_at_utc)
                VALUES ($character, $track, $level, $xp, $updated)
                ON CONFLICT(character_id, track_id) DO UPDATE SET
                    level = excluded.level,
                    experience = excluded.experience,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsert.Parameters.AddWithValue("$character", unchecked((long)characterId));
            upsert.Parameters.AddWithValue("$track", trackId);
            upsert.Parameters.AddWithValue("$level", level);
            upsert.Parameters.AddWithValue("$xp", xp);
            upsert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            upsert.ExecuteNonQuery();
            tx.Commit();
        }

        return Get(characterId);
    }

    private static (int Level, int Experience) LoadTrack(SqliteConnection connection, SqliteTransaction tx, ulong characterId, string trackId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT level, experience FROM character_progression WHERE character_id = $character AND track_id = $track LIMIT 1;";
        command.Parameters.AddWithValue("$character", unchecked((long)characterId));
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
        if (!TableExists(connection, "character_progression"))
        {
            CreateCharacterScopedProgressionTable(connection, "character_progression");
            return;
        }

        if (ColumnExists(connection, "character_progression", "account_id"))
        {
            MigrateLegacyAccountScopedTable(connection);
        }

        using (var index = connection.CreateCommand())
        {
            index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_character_progression_character_track ON character_progression(character_id, track_id);";
            index.ExecuteNonQuery();
        }
    }

    private static void MigrateLegacyAccountScopedTable(SqliteConnection connection)
    {
        CreateCharacterScopedProgressionTable(connection, "character_progression_v2");

        using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText =
                """
                INSERT OR REPLACE INTO character_progression_v2(character_id, track_id, level, experience, updated_at_utc)
                SELECT resolved.character_id, resolved.track_id, resolved.level, resolved.experience, resolved.updated_at_utc
                FROM (
                    SELECT
                        CASE
                            WHEN character_progression.character_id IS NOT NULL THEN character_progression.character_id
                            ELSE (
                                SELECT primary_character_id
                                FROM accounts
                                WHERE accounts.account_id = character_progression.account_id
                            )
                        END AS character_id,
                        character_progression.track_id,
                        character_progression.level,
                        character_progression.experience,
                        character_progression.updated_at_utc
                    FROM character_progression
                ) AS resolved
                WHERE resolved.character_id IS NOT NULL;
                """;
            migrate.ExecuteNonQuery();
        }

        using (var dropLegacy = connection.CreateCommand())
        {
            dropLegacy.CommandText = "DROP TABLE character_progression;";
            dropLegacy.ExecuteNonQuery();
        }

        using (var rename = connection.CreateCommand())
        {
            rename.CommandText = "ALTER TABLE character_progression_v2 RENAME TO character_progression;";
            rename.ExecuteNonQuery();
        }
    }

    private static void CreateCharacterScopedProgressionTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                character_id INTEGER NOT NULL,
                track_id TEXT NOT NULL,
                level INTEGER NOT NULL,
                experience INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (character_id, track_id)
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
