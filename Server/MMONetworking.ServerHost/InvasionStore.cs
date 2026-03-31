using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class InvasionStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public InvasionStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public InvasionBoardSnapshot GetBoard(ulong characterId)
    {
        if (characterId == 0)
        {
            throw new InvalidOperationException("characterId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return LoadBoard(connection, characterId);
        }
    }

    public InvasionBoardSnapshot Upsert(InvasionSnapshot invasion)
    {
        if (string.IsNullOrWhiteSpace(invasion.InvasionId))
        {
            throw new InvalidOperationException("invasionId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO invasions(
                    invasion_id, zone_id, mob_type_id, total_waves, current_wave, state, threat_level, starts_at_utc, ends_at_utc, updated_at_utc)
                VALUES(
                    $id, $zoneId, $mobTypeId, $totalWaves, $currentWave, $state, $threat, $starts, $ends, $updated)
                ON CONFLICT(invasion_id) DO UPDATE SET
                    zone_id = excluded.zone_id,
                    mob_type_id = excluded.mob_type_id,
                    total_waves = excluded.total_waves,
                    current_wave = excluded.current_wave,
                    state = excluded.state,
                    threat_level = excluded.threat_level,
                    starts_at_utc = excluded.starts_at_utc,
                    ends_at_utc = excluded.ends_at_utc,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", invasion.InvasionId.Trim());
            command.Parameters.AddWithValue("$zoneId", invasion.ZoneId);
            command.Parameters.AddWithValue("$mobTypeId", invasion.MobTypeId ?? string.Empty);
            command.Parameters.AddWithValue("$totalWaves", Math.Max(1, invasion.TotalWaves));
            command.Parameters.AddWithValue("$currentWave", Math.Max(0, invasion.CurrentWave));
            command.Parameters.AddWithValue("$state", string.IsNullOrWhiteSpace(invasion.State) ? "planned" : invasion.State.Trim());
            command.Parameters.AddWithValue("$threat", Math.Max(1, invasion.ThreatLevel));
            command.Parameters.AddWithValue("$starts", invasion.StartsAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$ends", invasion.EndsAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            InsertLog(connection, null, invasion.InvasionId.Trim(), 0UL, "upsert", "Upserted invasion.");
            return LoadBoard(connection, null);
        }
    }

    public InvasionBoardSnapshot AdvanceWave(string invasionId)
    {
        if (string.IsNullOrWhiteSpace(invasionId))
        {
            throw new InvalidOperationException("invasionId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var invasion = LoadInvasion(connection, tx, invasionId.Trim()) ?? throw new InvalidOperationException("Invasion not found.");

            var nextWave = Math.Min(invasion.TotalWaves, invasion.CurrentWave + 1);
            var nextState = nextWave >= invasion.TotalWaves ? "resolved" : "active";
            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    UPDATE invasions
                    SET current_wave = $nextWave,
                        state = $state,
                        updated_at_utc = $updated
                    WHERE invasion_id = $id;
                    """;
                command.Parameters.AddWithValue("$nextWave", nextWave);
                command.Parameters.AddWithValue("$state", nextState);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$id", invasionId.Trim());
                command.ExecuteNonQuery();
            }

            InsertLog(connection, tx, invasionId.Trim(), 0UL, "wave_advance", $"Advanced to wave {nextWave}.");
            tx.Commit();
            return LoadBoard(connection, null);
        }
    }

    public InvasionBoardSnapshot RecordKill(string invasionId, ulong characterId, int kills)
    {
        if (string.IsNullOrWhiteSpace(invasionId) || characterId == 0)
        {
            throw new InvalidOperationException("invasionId and characterId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO invasion_contributions(invasion_id, character_id, kills, is_claimed, updated_at_utc)
                VALUES($id, $characterId, $kills, 0, $updated)
                ON CONFLICT(invasion_id, character_id) DO UPDATE SET
                    kills = kills + excluded.kills,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", invasionId.Trim());
            command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
            command.Parameters.AddWithValue("$kills", Math.Max(1, kills));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            InsertLog(connection, null, invasionId.Trim(), characterId, "kill_credit", $"Added {Math.Max(1, kills)} kill credit.");
            return LoadBoard(connection, characterId);
        }
    }

    public InvasionClaimSnapshot Claim(string invasionId, ulong characterId)
    {
        if (string.IsNullOrWhiteSpace(invasionId) || characterId == 0)
        {
            throw new InvalidOperationException("invasionId and characterId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var invasion = LoadInvasion(connection, tx, invasionId.Trim()) ?? throw new InvalidOperationException("Invasion not found.");
            if (!string.Equals(invasion.State, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invasion must be resolved before claiming.");
            }

            var contribution = LoadContribution(connection, tx, invasionId.Trim(), characterId) ?? throw new InvalidOperationException("No contribution found.");
            if (contribution.IsClaimed)
            {
                throw new InvalidOperationException("Invasion rewards already claimed.");
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = "UPDATE invasion_contributions SET is_claimed = 1, updated_at_utc = $updated WHERE invasion_id = $id AND character_id = $characterId;";
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$id", invasionId.Trim());
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.ExecuteNonQuery();
            }
            InsertLog(connection, tx, invasionId.Trim(), characterId, "claim", $"Claimed with {contribution.Kills} kill credit.");
            tx.Commit();

            var xp = Math.Max(20, contribution.Kills * 10 + invasion.ThreatLevel * 20);
            var rep = Math.Max(5, contribution.Kills / 2 + invasion.ThreatLevel);
            return new InvasionClaimSnapshot(invasionId.Trim(), characterId, contribution.Kills, xp, "defenders", rep, $"Claimed invasion rewards for {invasionId}.");
        }
    }

    private static InvasionBoardSnapshot LoadBoard(SqliteConnection connection, ulong? characterId)
    {
        var invasions = new List<InvasionSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT invasion_id, zone_id, mob_type_id, total_waves, current_wave, state, threat_level, starts_at_utc, ends_at_utc
                FROM invasions
                ORDER BY starts_at_utc DESC, invasion_id ASC;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                invasions.Add(new InvasionSnapshot(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    DateTimeOffset.Parse(reader.GetString(7)),
                    DateTimeOffset.Parse(reader.GetString(8))));
            }
        }

        var contributions = new List<InvasionContributionSnapshot>();
        if (characterId.HasValue)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT invasion_id, character_id, kills, is_claimed, updated_at_utc
                FROM invasion_contributions
                WHERE character_id = $characterId
                ORDER BY invasion_id;
                """;
            command.Parameters.AddWithValue("$characterId", unchecked((long)characterId.Value));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                contributions.Add(new InvasionContributionSnapshot(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? 0UL : (ulong)reader.GetInt64(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3) != 0,
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
        }

        var logs = new List<InvasionLogSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, invasion_id, character_id, action_type, details, created_at_utc
                FROM invasion_logs
                ORDER BY id DESC
                LIMIT 120;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(new InvasionLogSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? 0UL : (ulong)reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    DateTimeOffset.Parse(reader.GetString(5))));
            }
        }

        return new InvasionBoardSnapshot(characterId ?? 0UL, invasions.ToArray(), contributions.ToArray(), logs.ToArray());
    }

    private static void InsertLog(SqliteConnection connection, SqliteTransaction? tx, string invasionId, ulong characterId, string actionType, string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO invasion_logs(invasion_id, character_id, action_type, details, created_at_utc)
            VALUES($id, $characterId, $actionType, $details, $created);
            """;
        command.Parameters.AddWithValue("$id", invasionId);
        command.Parameters.AddWithValue("$characterId", characterId == 0 ? DBNull.Value : unchecked((long)characterId));
        command.Parameters.AddWithValue("$actionType", actionType);
        command.Parameters.AddWithValue("$details", details);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static InvasionSnapshot? LoadInvasion(SqliteConnection connection, SqliteTransaction tx, string invasionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT invasion_id, zone_id, mob_type_id, total_waves, current_wave, state, threat_level, starts_at_utc, ends_at_utc
            FROM invasions
            WHERE invasion_id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", invasionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new InvasionSnapshot(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetInt32(6),
            DateTimeOffset.Parse(reader.GetString(7)),
            DateTimeOffset.Parse(reader.GetString(8)));
    }

    private static InvasionContributionSnapshot? LoadContribution(SqliteConnection connection, SqliteTransaction tx, string invasionId, ulong characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT kills, is_claimed, updated_at_utc
            FROM invasion_contributions
            WHERE invasion_id = $id AND character_id = $characterId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", invasionId);
        command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new InvasionContributionSnapshot(
            invasionId,
            characterId,
            reader.GetInt32(0),
            reader.GetInt32(1) != 0,
            DateTimeOffset.Parse(reader.GetString(2)));
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS invasions (
                    invasion_id TEXT PRIMARY KEY,
                    zone_id INTEGER NOT NULL,
                    mob_type_id TEXT NOT NULL,
                    total_waves INTEGER NOT NULL,
                    current_wave INTEGER NOT NULL,
                    state TEXT NOT NULL,
                    threat_level INTEGER NOT NULL,
                    starts_at_utc TEXT NOT NULL,
                    ends_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS invasion_contributions (
                    invasion_id TEXT NOT NULL,
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    kills INTEGER NOT NULL,
                    is_claimed INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY(invasion_id, account_id)
                );
                """;
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS invasion_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    invasion_id TEXT NOT NULL,
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    action_type TEXT NOT NULL,
                    details TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE invasion_contributions
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = invasion_contributions.account_id
                )
                WHERE character_id IS NULL;
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE invasion_logs
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = invasion_logs.account_id
                )
                WHERE character_id IS NULL;
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS ix_invasion_contributions_invasion_character
                ON invasion_contributions(invasion_id, character_id);
                """;
            command.ExecuteNonQuery();
        }
    }
}
