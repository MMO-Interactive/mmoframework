using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class CombatStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public CombatStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public CombatSnapshot GetSnapshot(int actionLimit = 200)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var combatants = new List<CombatantSnapshot>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT account_id, hp, max_hp, stamina, deaths FROM combatant_state ORDER BY account_id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    combatants.Add(new CombatantSnapshot(
                        reader.GetString(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4)));
                }
            }

            var actions = new List<CombatActionSnapshot>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, action_type, attacker_account_id, target_account_id, damage, remaining_hp, notes, created_at_utc FROM combat_actions ORDER BY id DESC LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", actionLimit);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    actions.Add(new CombatActionSnapshot(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                        DateTimeOffset.Parse(reader.GetString(7))));
                }
            }

            return new CombatSnapshot(combatants.ToArray(), actions.ToArray());
        }
    }

    public CombatSnapshot EnsureCombatant(string accountId)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            EnsureCombatantExists(connection, accountId);
            return GetSnapshot();
        }
    }

    public CombatSnapshot Attack(string attackerAccountId, string targetAccountId, int baseDamage)
    {
        if (string.IsNullOrWhiteSpace(attackerAccountId) || string.IsNullOrWhiteSpace(targetAccountId))
        {
            throw new InvalidOperationException("attacker and target are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            EnsureCombatantExists(connection, attackerAccountId, tx);
            EnsureCombatantExists(connection, targetAccountId, tx);

            var attacker = LoadState(connection, tx, attackerAccountId);
            var target = LoadState(connection, tx, targetAccountId);

            var damage = Math.Max(1, baseDamage + (attacker.Stamina / 20));
            target.HitPoints = Math.Max(0, target.HitPoints - damage);
            attacker.Stamina = Math.Max(0, attacker.Stamina - 8);

            var notes = "hit";
            if (target.HitPoints <= 0)
            {
                target.Deaths += 1;
                target.HitPoints = target.MaxHitPoints;
                target.Stamina = target.MaxHitPoints;
                notes = "defeated and respawned";
            }

            SaveState(connection, tx, attacker);
            SaveState(connection, tx, target);
            InsertAction(connection, tx, "attack", attackerAccountId, targetAccountId, damage, target.HitPoints, notes);
            tx.Commit();
        }

        return GetSnapshot();
    }

    private static void EnsureCombatantExists(SqliteConnection connection, string accountId, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO combatant_state(account_id, hp, max_hp, stamina, deaths, updated_at_utc)
            VALUES ($account, 100, 100, 100, 0, $updated)
            ON CONFLICT(account_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static MutableCombatant LoadState(SqliteConnection connection, SqliteTransaction tx, string accountId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT account_id, hp, max_hp, stamina, deaths FROM combatant_state WHERE account_id = $account LIMIT 1;";
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Combatant not found: {accountId}");
        }

        return new MutableCombatant
        {
            AccountId = reader.GetString(0),
            HitPoints = reader.GetInt32(1),
            MaxHitPoints = reader.GetInt32(2),
            Stamina = reader.GetInt32(3),
            Deaths = reader.GetInt32(4)
        };
    }

    private static void SaveState(SqliteConnection connection, SqliteTransaction tx, MutableCombatant state)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            UPDATE combatant_state
            SET hp = $hp,
                max_hp = $maxHp,
                stamina = $stamina,
                deaths = $deaths,
                updated_at_utc = $updated
            WHERE account_id = $account;
            """;
        command.Parameters.AddWithValue("$account", state.AccountId);
        command.Parameters.AddWithValue("$hp", state.HitPoints);
        command.Parameters.AddWithValue("$maxHp", state.MaxHitPoints);
        command.Parameters.AddWithValue("$stamina", state.Stamina);
        command.Parameters.AddWithValue("$deaths", state.Deaths);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertAction(SqliteConnection connection, SqliteTransaction tx, string actionType, string attacker, string target, int damage, int remainingHp, string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO combat_actions(action_type, attacker_account_id, target_account_id, damage, remaining_hp, notes, created_at_utc)
            VALUES ($type, $attacker, $target, $damage, $remaining, $notes, $created);
            """;
        command.Parameters.AddWithValue("$type", actionType);
        command.Parameters.AddWithValue("$attacker", attacker);
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$damage", damage);
        command.Parameters.AddWithValue("$remaining", remainingHp);
        command.Parameters.AddWithValue("$notes", notes);
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
                CREATE TABLE IF NOT EXISTS combatant_state (
                    account_id TEXT PRIMARY KEY,
                    hp INTEGER NOT NULL,
                    max_hp INTEGER NOT NULL,
                    stamina INTEGER NOT NULL,
                    deaths INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS combat_actions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    action_type TEXT NOT NULL,
                    attacker_account_id TEXT,
                    target_account_id TEXT,
                    damage INTEGER NOT NULL,
                    remaining_hp INTEGER NOT NULL,
                    notes TEXT,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private sealed class MutableCombatant
    {
        public string AccountId { get; set; } = string.Empty;
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; set; }
        public int Stamina { get; set; }
        public int Deaths { get; set; }
    }
}
