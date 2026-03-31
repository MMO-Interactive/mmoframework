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
                command.CommandText = "SELECT character_id, hp, max_hp, stamina, deaths FROM combatant_state ORDER BY character_id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    combatants.Add(new CombatantSnapshot(
                        reader.IsDBNull(0) ? 0UL : (ulong)reader.GetInt64(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4)));
                }
            }

            var actions = new List<CombatActionSnapshot>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, action_type, attacker_character_id, target_character_id, damage, remaining_hp, notes, created_at_utc FROM combat_actions ORDER BY id DESC LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", actionLimit);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    actions.Add(new CombatActionSnapshot(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? 0UL : (ulong)reader.GetInt64(2),
                        reader.IsDBNull(3) ? 0UL : (ulong)reader.GetInt64(3),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                        DateTimeOffset.Parse(reader.GetString(7))));
                }
            }

            return new CombatSnapshot(combatants.ToArray(), actions.ToArray());
        }
    }

    public CombatSnapshot EnsureCombatant(ulong characterId)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            EnsureCombatantExists(connection, characterId);
            return GetSnapshot();
        }
    }

    public CombatSnapshot Attack(ulong attackerCharacterId, ulong targetCharacterId, int baseDamage)
    {
        if (attackerCharacterId == 0 || targetCharacterId == 0)
        {
            throw new InvalidOperationException("attacker and target are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            EnsureCombatantExists(connection, attackerCharacterId, tx);
            EnsureCombatantExists(connection, targetCharacterId, tx);

            var attacker = LoadState(connection, tx, attackerCharacterId);
            var target = LoadState(connection, tx, targetCharacterId);

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
            InsertAction(connection, tx, "attack", attackerCharacterId, targetCharacterId, damage, target.HitPoints, notes);
            tx.Commit();
        }

        return GetSnapshot();
    }

    private static void EnsureCombatantExists(SqliteConnection connection, ulong characterId, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO combatant_state(character_id, hp, max_hp, stamina, deaths, updated_at_utc)
            VALUES ($character, 100, 100, 100, 0, $updated)
            ON CONFLICT(character_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$character", unchecked((long)characterId));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static MutableCombatant LoadState(SqliteConnection connection, SqliteTransaction tx, ulong characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT character_id, hp, max_hp, stamina, deaths FROM combatant_state WHERE character_id = $character LIMIT 1;";
        command.Parameters.AddWithValue("$character", unchecked((long)characterId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Combatant not found: {characterId}");
        }

        return new MutableCombatant
        {
            CharacterId = (ulong)reader.GetInt64(0),
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
            WHERE character_id = $character;
            """;
        command.Parameters.AddWithValue("$character", unchecked((long)state.CharacterId));
        command.Parameters.AddWithValue("$hp", state.HitPoints);
        command.Parameters.AddWithValue("$maxHp", state.MaxHitPoints);
        command.Parameters.AddWithValue("$stamina", state.Stamina);
        command.Parameters.AddWithValue("$deaths", state.Deaths);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertAction(SqliteConnection connection, SqliteTransaction tx, string actionType, ulong attacker, ulong target, int damage, int remainingHp, string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            INSERT INTO combat_actions(action_type, attacker_character_id, target_character_id, damage, remaining_hp, notes, created_at_utc)
            VALUES ($type, $attacker, $target, $damage, $remaining, $notes, $created);
            """;
        command.Parameters.AddWithValue("$type", actionType);
        command.Parameters.AddWithValue("$attacker", unchecked((long)attacker));
        command.Parameters.AddWithValue("$target", unchecked((long)target));
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
                    character_id INTEGER NULL,
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
                    attacker_character_id INTEGER NULL,
                    target_character_id INTEGER NULL,
                    damage INTEGER NOT NULL,
                    remaining_hp INTEGER NOT NULL,
                    notes TEXT,
                    created_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var migrateState = connection.CreateCommand())
        {
            migrateState.CommandText =
                """
                UPDATE combatant_state
                SET character_id = (
                    SELECT primary_character_id
                    FROM accounts
                    WHERE accounts.account_id = combatant_state.account_id
                )
                WHERE character_id IS NULL;
                """;
            migrateState.ExecuteNonQuery();
        }

        using (var migrateActions = connection.CreateCommand())
        {
            migrateActions.CommandText =
                """
                UPDATE combat_actions
                SET attacker_character_id = (
                        SELECT primary_character_id FROM accounts WHERE accounts.account_id = combat_actions.attacker_account_id
                    ),
                    target_character_id = (
                        SELECT primary_character_id FROM accounts WHERE accounts.account_id = combat_actions.target_account_id
                    )
                WHERE attacker_character_id IS NULL OR target_character_id IS NULL;
                """;
            migrateActions.ExecuteNonQuery();
        }

        using (var stateIndex = connection.CreateCommand())
        {
            stateIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_combatant_state_character ON combatant_state(character_id);";
            stateIndex.ExecuteNonQuery();
        }
    }

    private sealed class MutableCombatant
    {
        public ulong CharacterId { get; set; }
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; set; }
        public int Stamina { get; set; }
        public int Deaths { get; set; }
    }
}
