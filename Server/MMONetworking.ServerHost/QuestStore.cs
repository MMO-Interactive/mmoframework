using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class QuestStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public QuestStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public QuestBoardSnapshot GetBoard(ulong characterId)
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

    public QuestBoardSnapshot UpsertDefinition(QuestDefinitionSnapshot quest, string actor)
    {
        if (string.IsNullOrWhiteSpace(quest.QuestId))
        {
            throw new InvalidOperationException("questId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO quest_definitions(
                    quest_id, title, description, target_action, target_count, reward_xp, reward_reputation_faction, reward_reputation_amount, updated_at_utc, updated_by)
                VALUES(
                    $id, $title, $description, $targetAction, $targetCount, $rewardXp, $rewardFaction, $rewardAmount, $updated, $actor)
                ON CONFLICT(quest_id) DO UPDATE SET
                    title = excluded.title,
                    description = excluded.description,
                    target_action = excluded.target_action,
                    target_count = excluded.target_count,
                    reward_xp = excluded.reward_xp,
                    reward_reputation_faction = excluded.reward_reputation_faction,
                    reward_reputation_amount = excluded.reward_reputation_amount,
                    updated_at_utc = excluded.updated_at_utc,
                    updated_by = excluded.updated_by;
                """;
            command.Parameters.AddWithValue("$id", quest.QuestId.Trim());
            command.Parameters.AddWithValue("$title", quest.Title ?? string.Empty);
            command.Parameters.AddWithValue("$description", quest.Description ?? string.Empty);
            command.Parameters.AddWithValue("$targetAction", quest.TargetAction ?? string.Empty);
            command.Parameters.AddWithValue("$targetCount", Math.Max(1, quest.TargetCount));
            command.Parameters.AddWithValue("$rewardXp", Math.Max(0, quest.RewardExperience));
            command.Parameters.AddWithValue("$rewardFaction", quest.RewardReputationFaction ?? string.Empty);
            command.Parameters.AddWithValue("$rewardAmount", quest.RewardReputationAmount);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actor) ? "dashboard" : actor.Trim());
            command.ExecuteNonQuery();

            return LoadBoard(connection, null);
        }
    }

    public QuestBoardSnapshot RecordProgress(ulong characterId, string questId, int amount)
    {
        if (characterId == 0 || string.IsNullOrWhiteSpace(questId))
        {
            throw new InvalidOperationException("characterId and questId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            var definition = LoadDefinition(connection, tx, questId.Trim()) ?? throw new InvalidOperationException("Quest definition not found.");
            var clampedAmount = Math.Max(1, amount);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    INSERT INTO quest_progress(account_id, quest_id, progress_count, is_completed, is_claimed, updated_at_utc)
                    VALUES($characterId, $questId, $amount, 0, 0, $updated)
                    ON CONFLICT(character_id, quest_id) DO UPDATE SET
                        progress_count = progress_count + excluded.progress_count,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.Parameters.AddWithValue("$questId", questId.Trim());
                command.Parameters.AddWithValue("$amount", clampedAmount);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    UPDATE quest_progress
                    SET is_completed = CASE WHEN progress_count >= $target THEN 1 ELSE is_completed END,
                        progress_count = MIN(progress_count, $target),
                        updated_at_utc = $updated
                    WHERE character_id = $characterId AND quest_id = $questId;
                    """;
                command.Parameters.AddWithValue("$target", definition.TargetCount);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.Parameters.AddWithValue("$questId", questId.Trim());
                command.ExecuteNonQuery();
            }

            InsertEvent(connection, tx, characterId, questId.Trim(), "progress", clampedAmount, "Manual quest progress adjustment.");

            tx.Commit();
            return LoadBoard(connection, characterId);
        }
    }

    public QuestBoardSnapshot RecordAction(ulong characterId, string actionId, int amount)
    {
        if (characterId == 0 || string.IsNullOrWhiteSpace(actionId))
        {
            throw new InvalidOperationException("characterId and actionId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var clampedAmount = Math.Max(1, amount);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    INSERT INTO quest_progress(account_id, quest_id, progress_count, is_completed, is_claimed, updated_at_utc)
                    SELECT $characterId, quest_id, $amount, 0, 0, $updated
                    FROM quest_definitions
                    WHERE target_action = $action
                    ON CONFLICT(character_id, quest_id) DO UPDATE SET
                        progress_count = progress_count + excluded.progress_count,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.Parameters.AddWithValue("$amount", clampedAmount);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$action", actionId.Trim());
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    UPDATE quest_progress
                    SET is_completed = CASE
                        WHEN progress_count >= COALESCE((SELECT target_count FROM quest_definitions d WHERE d.quest_id = quest_progress.quest_id), progress_count)
                        THEN 1
                        ELSE is_completed
                    END,
                    progress_count = MIN(progress_count, COALESCE((SELECT target_count FROM quest_definitions d WHERE d.quest_id = quest_progress.quest_id), progress_count)),
                    updated_at_utc = $updated
                    WHERE character_id = $characterId
                      AND quest_id IN (
                        SELECT quest_id FROM quest_definitions WHERE target_action = $action
                      );
                    """;
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$action", actionId.Trim());
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    SELECT quest_id
                    FROM quest_definitions
                    WHERE target_action = $action;
                    """;
                command.Parameters.AddWithValue("$action", actionId.Trim());
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    InsertEvent(connection, tx, characterId, reader.GetString(0), "action_progress", clampedAmount, $"Applied action progress for {actionId.Trim()}.");
                }
            }

            tx.Commit();
            return LoadBoard(connection, characterId);
        }
    }

    public QuestClaimSnapshot Claim(ulong characterId, string questId)
    {
        if (characterId == 0 || string.IsNullOrWhiteSpace(questId))
        {
            throw new InvalidOperationException("characterId and questId are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            var definition = LoadDefinition(connection, tx, questId.Trim()) ?? throw new InvalidOperationException("Quest definition not found.");
            var progress = LoadProgress(connection, tx, characterId, questId.Trim()) ?? throw new InvalidOperationException("Quest not started for character.");
            if (!progress.IsCompleted)
            {
                throw new InvalidOperationException("Quest is not complete yet.");
            }

            if (progress.IsClaimed)
            {
                throw new InvalidOperationException("Quest already claimed.");
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText =
                    """
                    UPDATE quest_progress
                    SET is_claimed = 1,
                        updated_at_utc = $updated
                    WHERE character_id = $characterId AND quest_id = $questId;
                    """;
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
                command.Parameters.AddWithValue("$questId", questId.Trim());
                command.ExecuteNonQuery();
            }

            InsertEvent(connection, tx, characterId, questId.Trim(), "claim", 1, "Claimed quest rewards.");
            tx.Commit();
            return new QuestClaimSnapshot(
                characterId,
                definition.QuestId,
                definition.RewardExperience,
                definition.RewardReputationFaction,
                definition.RewardReputationAmount,
                $"Claimed rewards for {definition.Title}.");
        }
    }

    private static QuestBoardSnapshot LoadBoard(SqliteConnection connection, ulong? characterId)
    {
        var definitions = new List<QuestDefinitionSnapshot>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT quest_id, title, description, target_action, target_count, reward_xp, reward_reputation_faction, reward_reputation_amount
                FROM quest_definitions
                ORDER BY quest_id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                definitions.Add(new QuestDefinitionSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetInt32(7)));
            }
        }

        var progress = new List<QuestProgressSnapshot>();
        if (characterId.HasValue)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT quest_id, progress_count, is_completed, is_claimed, updated_at_utc
                FROM quest_progress
                WHERE character_id = $characterId
                ORDER BY quest_id;
                """;
            command.Parameters.AddWithValue("$characterId", unchecked((long)characterId.Value));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                progress.Add(new QuestProgressSnapshot(
                    characterId.Value,
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2) != 0,
                    reader.GetInt32(3) != 0,
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
        }

        var events = new List<QuestEventSnapshot>();
        if (characterId.HasValue)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, character_id, quest_id, event_type, amount, notes, created_at_utc
                FROM quest_events
                WHERE character_id = $characterId
                ORDER BY id DESC
                LIMIT 50;
                """;
            command.Parameters.AddWithValue("$characterId", unchecked((long)characterId.Value));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new QuestEventSnapshot(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? 0UL : (ulong)reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6))));
            }
        }

        return new QuestBoardSnapshot(characterId ?? 0UL, definitions.ToArray(), progress.ToArray(), events.ToArray());
    }

    private static void InsertEvent(SqliteConnection connection, SqliteTransaction? tx, ulong characterId, string questId, string eventType, int amount, string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO quest_events(character_id, quest_id, event_type, amount, notes, created_at_utc)
            VALUES ($characterId, $questId, $eventType, $amount, $notes, $created);
            """;
        command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        command.Parameters.AddWithValue("$questId", questId);
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static QuestDefinitionSnapshot? LoadDefinition(SqliteConnection connection, SqliteTransaction tx, string questId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT quest_id, title, description, target_action, target_count, reward_xp, reward_reputation_faction, reward_reputation_amount
            FROM quest_definitions
            WHERE quest_id = $questId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$questId", questId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new QuestDefinitionSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetInt32(7));
    }

    private static QuestProgressSnapshot? LoadProgress(SqliteConnection connection, SqliteTransaction tx, ulong characterId, string questId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT progress_count, is_completed, is_claimed, updated_at_utc
            FROM quest_progress
            WHERE character_id = $characterId AND quest_id = $questId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        command.Parameters.AddWithValue("$questId", questId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new QuestProgressSnapshot(
            characterId,
            questId,
            reader.GetInt32(0),
            reader.GetInt32(1) != 0,
            reader.GetInt32(2) != 0,
            DateTimeOffset.Parse(reader.GetString(3)));
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS quest_definitions (
                    quest_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    target_action TEXT NOT NULL,
                    target_count INTEGER NOT NULL,
                    reward_xp INTEGER NOT NULL,
                    reward_reputation_faction TEXT NOT NULL,
                    reward_reputation_amount INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    updated_by TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS quest_progress (
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    quest_id TEXT NOT NULL,
                    progress_count INTEGER NOT NULL,
                    is_completed INTEGER NOT NULL,
                    is_claimed INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY(account_id, quest_id)
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS quest_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_id TEXT NOT NULL,
                    character_id INTEGER NULL,
                    quest_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    amount INTEGER NOT NULL,
                    notes TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE quest_progress
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = quest_progress.account_id
                )
                WHERE character_id IS NULL;
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE quest_events
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = quest_events.account_id
                )
                WHERE character_id IS NULL;
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS ix_quest_progress_character_quest
                ON quest_progress(character_id, quest_id);
                """;
            command.ExecuteNonQuery();
        }
    }
}
