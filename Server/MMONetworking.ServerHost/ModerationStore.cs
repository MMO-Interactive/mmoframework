using System;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class ModerationStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public ModerationStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public bool IsAccountBanned(string accountId)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT is_banned FROM account_moderation WHERE account_id = $accountId LIMIT 1;";
            command.Parameters.AddWithValue("$accountId", accountId);
            var value = command.ExecuteScalar();
            return value is long number && number == 1;
        }
    }

    public void SetAccountState(string accountId, bool isMuted, bool isBanned, string reason, string actor)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var upsert = connection.CreateCommand())
            {
                upsert.Transaction = transaction;
                upsert.CommandText =
                    """
                    INSERT INTO account_moderation(account_id, is_muted, is_banned, reason, updated_by, updated_at_utc)
                    VALUES ($accountId, $isMuted, $isBanned, $reason, $actor, $updated)
                    ON CONFLICT(account_id) DO UPDATE SET
                        is_muted = excluded.is_muted,
                        is_banned = excluded.is_banned,
                        reason = excluded.reason,
                        updated_by = excluded.updated_by,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                upsert.Parameters.AddWithValue("$accountId", accountId);
                upsert.Parameters.AddWithValue("$isMuted", isMuted ? 1 : 0);
                upsert.Parameters.AddWithValue("$isBanned", isBanned ? 1 : 0);
                upsert.Parameters.AddWithValue("$reason", reason ?? string.Empty);
                upsert.Parameters.AddWithValue("$actor", actor ?? "system");
                upsert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                upsert.ExecuteNonQuery();
            }

            InsertAction(connection, transaction, isBanned ? "ban" : isMuted ? "mute" : "clear", accountId, null, null, reason, actor);
            transaction.Commit();
        }
    }

    public void RecordKick(Guid sessionId, ulong playerId, string accountId, string reason, string actor)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            InsertAction(connection, transaction, "kick", accountId, sessionId, playerId, reason, actor);
            transaction.Commit();
        }
    }

    public ModerationSnapshot CreateSnapshot(int actionLimit = 200)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var accounts = Enumerable.Empty<AccountModerationSnapshot>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT account_id, is_muted, is_banned, reason, updated_by, updated_at_utc FROM account_moderation ORDER BY account_id;";
                using var reader = command.ExecuteReader();
                var list = new System.Collections.Generic.List<AccountModerationSnapshot>();
                while (reader.Read())
                {
                    list.Add(new AccountModerationSnapshot(
                        reader.GetString(0),
                        reader.GetInt64(1) == 1,
                        reader.GetInt64(2) == 1,
                        reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        reader.IsDBNull(4) ? "system" : reader.GetString(4),
                        DateTimeOffset.Parse(reader.GetString(5))));
                }

                accounts = list;
            }

            var actions = Enumerable.Empty<ModerationActionSnapshot>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT id, action_type, account_id, session_id, player_id, reason, actor, created_at_utc FROM moderation_actions ORDER BY id DESC LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", actionLimit);
                using var reader = command.ExecuteReader();
                var list = new System.Collections.Generic.List<ModerationActionSnapshot>();
                while (reader.Read())
                {
                    list.Add(new ModerationActionSnapshot(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                        reader.IsDBNull(4) ? null : (ulong?)reader.GetInt64(4),
                        reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        reader.IsDBNull(6) ? "system" : reader.GetString(6),
                        DateTimeOffset.Parse(reader.GetString(7))));
                }

                actions = list;
            }

            return new ModerationSnapshot(accounts.ToArray(), actions.ToArray());
        }
    }

    private static void InsertAction(SqliteConnection connection, SqliteTransaction transaction, string actionType, string accountId, Guid? sessionId, ulong? playerId, string reason, string actor)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO moderation_actions(action_type, account_id, session_id, player_id, reason, actor, created_at_utc)
            VALUES ($type, $accountId, $sessionId, $playerId, $reason, $actor, $created);
            """;
        command.Parameters.AddWithValue("$type", actionType);
        command.Parameters.AddWithValue("$accountId", accountId ?? string.Empty);
        command.Parameters.AddWithValue("$sessionId", sessionId.HasValue ? sessionId.Value.ToString() : (object?)DBNull.Value);
        command.Parameters.AddWithValue("$playerId", playerId.HasValue ? (long)playerId.Value : (object?)DBNull.Value);
        command.Parameters.AddWithValue("$reason", reason ?? string.Empty);
        command.Parameters.AddWithValue("$actor", actor ?? "system");
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS account_moderation (
                    account_id TEXT PRIMARY KEY,
                    is_muted INTEGER NOT NULL,
                    is_banned INTEGER NOT NULL,
                    reason TEXT,
                    updated_by TEXT,
                    updated_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS moderation_actions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    action_type TEXT NOT NULL,
                    account_id TEXT,
                    session_id TEXT,
                    player_id INTEGER,
                    reason TEXT,
                    actor TEXT,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }
}
