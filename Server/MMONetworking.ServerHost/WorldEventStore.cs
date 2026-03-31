using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class WorldEventStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public WorldEventStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public WorldEventBoardSnapshot GetBoard(ulong characterId)
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

    public WorldEventBoardSnapshot Upsert(WorldEventSnapshot worldEvent)
    {
        if (string.IsNullOrWhiteSpace(worldEvent.EventId))
        {
            throw new InvalidOperationException("eventId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO world_events(
                    event_id, title, description, state, starts_at_utc, ends_at_utc, reward_xp, reward_reputation_faction, reward_reputation_amount, created_at_utc, updated_at_utc)
                VALUES(
                    $eventId, $title, $description, $state, $starts, $ends, $rewardXp, $rewardFaction, $rewardAmount, $created, $updated)
                ON CONFLICT(event_id) DO UPDATE SET
                    title = excluded.title,
                    description = excluded.description,
                    state = excluded.state,
                    starts_at_utc = excluded.starts_at_utc,
                    ends_at_utc = excluded.ends_at_utc,
                    reward_xp = excluded.reward_xp,
                    reward_reputation_faction = excluded.reward_reputation_faction,
                    reward_reputation_amount = excluded.reward_reputation_amount,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$eventId", worldEvent.EventId.Trim());
            command.Parameters.AddWithValue("$title", worldEvent.Title ?? string.Empty);
            command.Parameters.AddWithValue("$description", worldEvent.Description ?? string.Empty);
            command.Parameters.AddWithValue("$state", string.IsNullOrWhiteSpace(worldEvent.State) ? "draft" : worldEvent.State.Trim());
            command.Parameters.AddWithValue("$starts", worldEvent.StartsAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$ends", worldEvent.EndsAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$rewardXp", Math.Max(0, worldEvent.BaseRewardExperience));
            command.Parameters.AddWithValue("$rewardFaction", worldEvent.RewardReputationFaction ?? string.Empty);
            command.Parameters.AddWithValue("$rewardAmount", worldEvent.RewardReputationAmount);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            InsertLog(connection, null, worldEvent.EventId.Trim(), 0UL, "upsert", "Upserted world event definition.");
            return LoadBoard(connection, null);
        }
    }

    public WorldEventBoardSnapshot SetState(string eventId, string state)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(state))
        {
            throw new InvalidOperationException("eventId and state are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE world_events SET state = $state, updated_at_utc = $updated WHERE event_id = $eventId;";
            command.Parameters.AddWithValue("$state", state.Trim());
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$eventId", eventId.Trim());
            command.ExecuteNonQuery();
            InsertLog(connection, null, eventId.Trim(), 0UL, "state", $"State set to {state.Trim()}.");
            return LoadBoard(connection, null);
        }
    }

    public WorldEventBoardSnapshot AddContribution(string eventId, ulong characterId, int amount)
    {
        if (string.IsNullOrWhiteSpace(eventId) || characterId == 0)
        {
            throw new InvalidOperationException("eventId and characterId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO world_event_participation(event_id, character_id, contribution, is_claimed, updated_at_utc)
                VALUES($eventId, $characterId, $amount, 0, $updated)
                ON CONFLICT(event_id, character_id) DO UPDATE SET
                    contribution = contribution + excluded.contribution,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$eventId", eventId.Trim());
            command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
            command.Parameters.AddWithValue("$amount", Math.Max(1, amount));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            InsertLog(connection, null, eventId.Trim(), characterId, "contribution", $"Added contribution amount {Math.Max(1, amount)}.");
            return LoadBoard(connection, characterId);
        }
    }

    public WorldEventClaimSnapshot Claim(string eventId, ulong characterId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || characterId == 0)
        {
            throw new InvalidOperationException("eventId and characterId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            var worldEvent = LoadEvent(connection, tx, eventId.Trim()) ?? throw new InvalidOperationException("World event not found.");
            if (!string.Equals(worldEvent.State, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("World event must be in resolved state before claiming.");
            }

            var participation = LoadParticipation(connection, tx, eventId.Trim(), characterId) ?? throw new InvalidOperationException("No participation found for this character.");
            if (participation.IsClaimed)
            {
                throw new InvalidOperationException("Rewards already claimed.");
            }

            if (participation.Contribution <= 0)
            {
                throw new InvalidOperationException("Contribution must be greater than zero.");
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = "UPDATE world_event_participation SET is_claimed = 1, updated_at_utc = $updated WHERE event_id = $eventId AND character_id = $characterId;";
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$eventId", eventId.Trim());
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.ExecuteNonQuery();
            }
            InsertLog(connection, tx, eventId.Trim(), characterId, "claim", $"Claimed event rewards with contribution {participation.Contribution}.");

            tx.Commit();

            var awardedXp = worldEvent.BaseRewardExperience + (participation.Contribution * 2);
            var awardedRep = worldEvent.RewardReputationAmount == 0
                ? 0
                : worldEvent.RewardReputationAmount + Math.Max(1, participation.Contribution / 5);

            return new WorldEventClaimSnapshot(
                eventId.Trim(),
                characterId,
                participation.Contribution,
                awardedXp,
                worldEvent.RewardReputationFaction,
                awardedRep,
                $"Claimed rewards from {worldEvent.Title}.");
        }
    }

    public WorldEventBoardSnapshot AdvanceLifecycle(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    UPDATE world_events
                    SET state = 'active',
                        updated_at_utc = $updated
                    WHERE state = 'draft'
                      AND starts_at_utc <= $now;
                    """;
                command.Parameters.AddWithValue("$updated", nowUtc.ToString("O"));
                command.Parameters.AddWithValue("$now", nowUtc.ToString("O"));
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    UPDATE world_events
                    SET state = 'resolved',
                        updated_at_utc = $updated
                    WHERE state = 'active'
                      AND ends_at_utc <= $now;
                    """;
                command.Parameters.AddWithValue("$updated", nowUtc.ToString("O"));
                command.Parameters.AddWithValue("$now", nowUtc.ToString("O"));
                command.ExecuteNonQuery();
            }
            InsertLog(connection, null, "__lifecycle__", 0UL, "lifecycle", $"Ran lifecycle tick at {nowUtc:O}.");

            return LoadBoard(connection, null);
        }
    }

    public WorldEventLeaderboardEntrySnapshot[] GetLeaderboard(string eventId, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new InvalidOperationException("eventId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var list = new List<WorldEventLeaderboardEntrySnapshot>();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT character_id, contribution, is_claimed
                FROM world_event_participation
                WHERE event_id = $eventId
                ORDER BY contribution DESC, character_id ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$eventId", eventId.Trim());
            command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
            using var reader = command.ExecuteReader();
            var rank = 1;
            while (reader.Read())
            {
                list.Add(new WorldEventLeaderboardEntrySnapshot(
                    rank++,
                    reader.IsDBNull(0) ? 0UL : (ulong)reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2) != 0));
            }

            return list.ToArray();
        }
    }

    private static WorldEventBoardSnapshot LoadBoard(SqliteConnection connection, ulong? characterId)
    {
        var events = new List<WorldEventSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT event_id, title, description, state, starts_at_utc, ends_at_utc, reward_xp, reward_reputation_faction, reward_reputation_amount, created_at_utc
                FROM world_events
                ORDER BY starts_at_utc DESC, event_id ASC;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new WorldEventSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    DateTimeOffset.Parse(reader.GetString(4)),
                    DateTimeOffset.Parse(reader.GetString(5)),
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetInt32(8),
                    DateTimeOffset.Parse(reader.GetString(9))));
            }
        }

        var participation = new List<WorldEventParticipationSnapshot>();
        if (characterId.HasValue)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT event_id, character_id, contribution, is_claimed, updated_at_utc
                FROM world_event_participation
                WHERE character_id = $characterId
                ORDER BY event_id;
                """;
            command.Parameters.AddWithValue("$characterId", unchecked((long)characterId.Value));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                participation.Add(new WorldEventParticipationSnapshot(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? 0UL : (ulong)reader.GetInt64(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3) != 0,
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
        }

        var logs = new List<WorldEventLogSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, event_id, character_id, action_type, details, created_at_utc
                FROM world_event_logs
                ORDER BY id DESC
                LIMIT 120;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(new WorldEventLogSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? 0UL : (ulong)reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    DateTimeOffset.Parse(reader.GetString(5))));
            }
        }

        return new WorldEventBoardSnapshot(characterId ?? 0UL, events.ToArray(), participation.ToArray(), logs.ToArray());
    }

    private static void InsertLog(SqliteConnection connection, SqliteTransaction? tx, string eventId, ulong characterId, string actionType, string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO world_event_logs(event_id, character_id, action_type, details, created_at_utc)
            VALUES($eventId, $characterId, $actionType, $details, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$characterId", characterId == 0 ? DBNull.Value : unchecked((long)characterId));
        command.Parameters.AddWithValue("$actionType", actionType);
        command.Parameters.AddWithValue("$details", details);
        command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static WorldEventSnapshot? LoadEvent(SqliteConnection connection, SqliteTransaction tx, string eventId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT event_id, title, description, state, starts_at_utc, ends_at_utc, reward_xp, reward_reputation_faction, reward_reputation_amount, created_at_utc
            FROM world_events
            WHERE event_id = $eventId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$eventId", eventId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new WorldEventSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetInt32(8),
            DateTimeOffset.Parse(reader.GetString(9)));
    }

    private static WorldEventParticipationSnapshot? LoadParticipation(SqliteConnection connection, SqliteTransaction tx, string eventId, ulong characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT contribution, is_claimed, updated_at_utc
            FROM world_event_participation
            WHERE event_id = $eventId AND character_id = $characterId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new WorldEventParticipationSnapshot(
            eventId,
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
                CREATE TABLE IF NOT EXISTS world_events (
                    event_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    state TEXT NOT NULL,
                    starts_at_utc TEXT NOT NULL,
                    ends_at_utc TEXT NOT NULL,
                    reward_xp INTEGER NOT NULL,
                    reward_reputation_faction TEXT NOT NULL,
                    reward_reputation_amount INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS world_event_participation (
                    event_id TEXT NOT NULL,
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    contribution INTEGER NOT NULL,
                    is_claimed INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY(event_id, account_id)
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS world_event_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL,
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
                UPDATE world_event_participation
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = world_event_participation.account_id
                )
                WHERE character_id IS NULL;
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE world_event_logs
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = world_event_logs.account_id
                )
                WHERE character_id IS NULL;
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS ix_world_event_participation_event_character
                ON world_event_participation(event_id, character_id);
                """;
            command.ExecuteNonQuery();
        }
    }
}
