using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class AccountStore
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private readonly string _connectionString;

    public AccountStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        Initialize();
    }

    public AccountAuthResult Authenticate(string accountName, string password, AccountAuthMode authMode)
    {
        var normalizedAccountName = NormalizeAccountName(accountName);
        ValidatePassword(password);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        return authMode switch
        {
            AccountAuthMode.Login => AuthenticateExisting(connection, normalizedAccountName, password),
            AccountAuthMode.Register => RegisterNew(connection, normalizedAccountName, password),
            _ => new AccountAuthResult(false, string.Empty, normalizedAccountName, 0, "Unsupported account auth mode.")
        };
    }

    public AccountSnapshot[] GetAccounts()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, account_name, primary_character_id, created_at_utc, last_login_at_utc
            FROM accounts
            ORDER BY account_name;
            """;

        using var reader = command.ExecuteReader();
        var accounts = new List<AccountSnapshot>();
        while (reader.Read())
        {
            accounts.Add(new AccountSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                (ulong)reader.GetInt64(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return accounts.ToArray();
    }

    public AccountCharacterListSnapshot GetCharacterList(string accountName, string password)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var auth = AuthenticateExisting(connection, NormalizeAccountName(accountName), password);
        if (!auth.Success)
        {
            throw new InvalidOperationException(auth.ErrorText);
        }

        return LoadCharacterList(connection, auth.AccountId, auth.AccountName);
    }

    public AccountCharacterListSnapshot CreateCharacter(string accountName, string password, string characterName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var auth = AuthenticateExisting(connection, NormalizeAccountName(accountName), password);
        if (!auth.Success)
        {
            throw new InvalidOperationException(auth.ErrorText);
        }

        var normalizedCharacterName = NormalizeCharacterName(characterName);
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = """
                SELECT 1
                FROM characters
                WHERE account_id = $accountId
                  AND lower(character_name) = $characterName
                LIMIT 1;
                """;
            exists.Parameters.AddWithValue("$accountId", auth.AccountId);
            exists.Parameters.AddWithValue("$characterName", normalizedCharacterName.ToLowerInvariant());
            if (exists.ExecuteScalar() != null)
            {
                throw new InvalidOperationException("Character name already exists on this account.");
            }
        }

        var characterId = GenerateCharacterId();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO characters(character_id, account_id, character_name, created_at_utc, last_selected_at_utc)
            VALUES ($characterId, $accountId, $characterName, $createdAtUtc, NULL);
            """;
        insert.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        insert.Parameters.AddWithValue("$accountId", auth.AccountId);
        insert.Parameters.AddWithValue("$characterName", normalizedCharacterName);
        insert.Parameters.AddWithValue("$createdAtUtc", DateTime.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();

        return LoadCharacterList(connection, auth.AccountId, auth.AccountName);
    }

    public AccountCharacterListSnapshot SelectCharacter(string accountName, string password, ulong characterId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var auth = AuthenticateExisting(connection, NormalizeAccountName(accountName), password);
        if (!auth.Success)
        {
            throw new InvalidOperationException(auth.ErrorText);
        }

        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = """
                SELECT 1
                FROM characters
                WHERE account_id = $accountId
                  AND character_id = $characterId
                LIMIT 1;
                """;
            exists.Parameters.AddWithValue("$accountId", auth.AccountId);
            exists.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
            if (exists.ExecuteScalar() == null)
            {
                throw new InvalidOperationException("Character not found on this account.");
            }
        }

        SetPrimaryCharacter(connection, auth.AccountId, characterId);
        return LoadCharacterList(connection, auth.AccountId, auth.AccountName);
    }

    public ulong GetPrimaryCharacterId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("accountId is required.");
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT primary_character_id
            FROM accounts
            WHERE account_id = $accountId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.Trim());

        var value = command.ExecuteScalar();
        if (value == null || value == DBNull.Value)
        {
            throw new InvalidOperationException("Account not found.");
        }

        return (ulong)(long)value;
    }

    private static AccountAuthResult AuthenticateExisting(SqliteConnection connection, string normalizedAccountName, string password)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, account_name, primary_character_id, password_salt, password_hash
            FROM accounts
            WHERE account_name = $accountName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountName", normalizedAccountName);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new AccountAuthResult(false, string.Empty, normalizedAccountName, 0, "Account does not exist.");
        }

        var accountId = reader.GetString(0);
        var accountName = reader.GetString(1);
        var characterId = (ulong)reader.GetInt64(2);
        var salt = (byte[])reader["password_salt"];
        var expectedHash = (byte[])reader["password_hash"];
        var actualHash = HashPassword(password, salt);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            return new AccountAuthResult(false, accountId, accountName, characterId, "Invalid credentials.");
        }

        UpdateLastLogin(connection, accountId);
        return new AccountAuthResult(true, accountId, accountName, characterId, string.Empty);
    }

    private static AccountAuthResult RegisterNew(SqliteConnection connection, string normalizedAccountName, string password)
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM accounts WHERE account_name = $accountName LIMIT 1;";
            exists.Parameters.AddWithValue("$accountName", normalizedAccountName);
            if (exists.ExecuteScalar() != null)
            {
                return new AccountAuthResult(false, string.Empty, normalizedAccountName, 0, "Account already exists.");
            }
        }

        var accountId = Guid.NewGuid().ToString("N");
        var characterId = GenerateCharacterId();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        var now = DateTime.UtcNow.ToString("O");

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO accounts(account_id, account_name, primary_character_id, password_salt, password_hash, created_at_utc, last_login_at_utc)
            VALUES ($accountId, $accountName, $characterId, $salt, $hash, $createdAtUtc, $lastLoginAtUtc);
            """;
        insert.Parameters.AddWithValue("$accountId", accountId);
        insert.Parameters.AddWithValue("$accountName", normalizedAccountName);
        insert.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        insert.Parameters.AddWithValue("$salt", salt);
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$createdAtUtc", now);
        insert.Parameters.AddWithValue("$lastLoginAtUtc", now);
        insert.ExecuteNonQuery();

        InsertCharacter(connection, accountId, characterId, normalizedAccountName, now, now);

        return new AccountAuthResult(true, accountId, normalizedAccountName, characterId, string.Empty);
    }

    private static void UpdateLastLogin(SqliteConnection connection, string accountId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE accounts
            SET last_login_at_utc = $lastLoginAtUtc
            WHERE account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$lastLoginAtUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$accountId", accountId);
        command.ExecuteNonQuery();
    }

    private static byte[] HashPassword(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

    private static ulong GenerateCharacterId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        bytes[7] &= 0x7F;
        var value = BitConverter.ToUInt64(bytes);
        return value == 0 ? 1UL : value;
    }

    private static string NormalizeCharacterName(string characterName)
    {
        var value = (characterName ?? string.Empty).Trim();
        if (value.Length < 3 || value.Length > 24)
        {
            throw new InvalidOperationException("Character name must be between 3 and 24 characters.");
        }

        foreach (var character in value)
        {
            var isAlphaNumeric = char.IsLetterOrDigit(character);
            if (!isAlphaNumeric && character != ' ' && character != '-' && character != '\'')
            {
                throw new InvalidOperationException("Character name may only contain letters, numbers, spaces, apostrophes, and hyphens.");
            }
        }

        return value;
    }

    private static string NormalizeAccountName(string accountName)
    {
        var value = (accountName ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length < 3 || value.Length > 32)
        {
            throw new InvalidOperationException("Account name must be between 3 and 32 characters.");
        }

        foreach (var character in value)
        {
            var isAlphaNumeric = character is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!isAlphaNumeric && character != '_' && character != '-' && character != '.')
            {
                throw new InvalidOperationException("Account name may only contain letters, numbers, '.', '-', and '_'.");
            }
        }

        return value;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts
            (
                account_id TEXT PRIMARY KEY,
                password_salt BLOB NOT NULL,
                password_hash BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_login_at_utc TEXT NULL
            );
            """;
        command.ExecuteNonQuery();

        using var characters = connection.CreateCommand();
        characters.CommandText = """
            CREATE TABLE IF NOT EXISTS characters
            (
                character_id INTEGER PRIMARY KEY,
                account_id TEXT NOT NULL,
                character_name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_selected_at_utc TEXT NULL
            );
            """;
        characters.ExecuteNonQuery();

        EnsureColumnExists(connection, "accounts", "account_name", "TEXT NULL");
        EnsureColumnExists(connection, "accounts", "primary_character_id", "INTEGER NULL");

        using var migration = connection.CreateCommand();
        migration.CommandText = """
            SELECT account_id, account_name, primary_character_id
            FROM accounts;
            """;

        var pendingRows = new List<(string AccountId, string? AccountName, long? CharacterId)>();
        using (var reader = migration.ExecuteReader())
        {
            while (reader.Read())
            {
                pendingRows.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2)));
            }
        }

        foreach (var row in pendingRows)
        {
            var accountName = string.IsNullOrWhiteSpace(row.AccountName) ? row.AccountId : row.AccountName.Trim().ToLowerInvariant();
            var characterId = row.CharacterId.HasValue && row.CharacterId.Value > 0
                ? (ulong)row.CharacterId.Value
                : GenerateCharacterId();

            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE accounts
                SET account_name = $accountName,
                    primary_character_id = $characterId
                WHERE account_id = $accountId;
                """;
            update.Parameters.AddWithValue("$accountName", accountName);
            update.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
            update.Parameters.AddWithValue("$accountId", row.AccountId);
            update.ExecuteNonQuery();
        }

        using var index = connection.CreateCommand();
        index.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_accounts_account_name
            ON accounts(account_name);
            """;
        index.ExecuteNonQuery();

        using var characterIndex = connection.CreateCommand();
        characterIndex.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_characters_account_name
            ON characters(account_id, character_name);
            """;
        characterIndex.ExecuteNonQuery();

        using var characterLoad = connection.CreateCommand();
        characterLoad.CommandText = """
            SELECT account_id, account_name, primary_character_id
            FROM accounts;
            """;

        var accountRows = new List<(string AccountId, string AccountName, long? CharacterId)>();
        using (var reader = characterLoad.ExecuteReader())
        {
            while (reader.Read())
            {
                accountRows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2)));
            }
        }

        foreach (var row in accountRows)
        {
            using var countCharacters = connection.CreateCommand();
            countCharacters.CommandText = "SELECT COUNT(1) FROM characters WHERE account_id = $accountId;";
            countCharacters.Parameters.AddWithValue("$accountId", row.AccountId);
            var existingCount = Convert.ToInt32(countCharacters.ExecuteScalar());

            var selectedCharacterId = row.CharacterId.HasValue && row.CharacterId.Value > 0
                ? (ulong)row.CharacterId.Value
                : GenerateCharacterId();

            if (existingCount == 0)
            {
                InsertCharacter(connection, row.AccountId, selectedCharacterId, row.AccountName, DateTime.UtcNow.ToString("O"), DateTime.UtcNow.ToString("O"));
            }

            SetPrimaryCharacter(connection, row.AccountId, selectedCharacterId);
        }
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static void InsertCharacter(SqliteConnection connection, string accountId, ulong characterId, string characterName, string createdAtUtc, string? lastSelectedAtUtc)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO characters(character_id, account_id, character_name, created_at_utc, last_selected_at_utc)
            VALUES ($characterId, $accountId, $characterName, $createdAtUtc, $lastSelectedAtUtc);
            """;
        insert.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        insert.Parameters.AddWithValue("$accountId", accountId);
        insert.Parameters.AddWithValue("$characterName", NormalizeCharacterName(characterName));
        insert.Parameters.AddWithValue("$createdAtUtc", createdAtUtc);
        insert.Parameters.AddWithValue("$lastSelectedAtUtc", string.IsNullOrWhiteSpace(lastSelectedAtUtc) ? DBNull.Value : lastSelectedAtUtc);
        insert.ExecuteNonQuery();
    }

    private static void SetPrimaryCharacter(SqliteConnection connection, string accountId, ulong characterId)
    {
        var now = DateTime.UtcNow.ToString("O");

        using (var updateAccount = connection.CreateCommand())
        {
            updateAccount.CommandText = """
                UPDATE accounts
                SET primary_character_id = $characterId
                WHERE account_id = $accountId;
                """;
            updateAccount.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
            updateAccount.Parameters.AddWithValue("$accountId", accountId);
            updateAccount.ExecuteNonQuery();
        }

        using var updateCharacter = connection.CreateCommand();
        updateCharacter.CommandText = """
            UPDATE characters
            SET last_selected_at_utc = $lastSelectedAtUtc
            WHERE account_id = $accountId
              AND character_id = $characterId;
            """;
        updateCharacter.Parameters.AddWithValue("$lastSelectedAtUtc", now);
        updateCharacter.Parameters.AddWithValue("$accountId", accountId);
        updateCharacter.Parameters.AddWithValue("$characterId", unchecked((long)characterId));
        updateCharacter.ExecuteNonQuery();
    }

    private static AccountCharacterListSnapshot LoadCharacterList(SqliteConnection connection, string accountId, string accountName)
    {
        ulong selectedCharacterId;
        using (var selected = connection.CreateCommand())
        {
            selected.CommandText = "SELECT primary_character_id FROM accounts WHERE account_id = $accountId LIMIT 1;";
            selected.Parameters.AddWithValue("$accountId", accountId);
            var value = selected.ExecuteScalar();
            selectedCharacterId = value == null || value == DBNull.Value ? 0UL : (ulong)(long)value;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id, character_name, created_at_utc, last_selected_at_utc
            FROM characters
            WHERE account_id = $accountId
            ORDER BY created_at_utc, character_name;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);

        using var reader = command.ExecuteReader();
        var characters = new List<CharacterSummarySnapshot>();
        while (reader.Read())
        {
            characters.Add(new CharacterSummarySnapshot(
                (ulong)reader.GetInt64(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
        }

        return new AccountCharacterListSnapshot(accountId, accountName, selectedCharacterId, characters.ToArray());
    }
}

public readonly record struct AccountAuthResult(bool Success, string AccountId, string AccountName, ulong CharacterId, string ErrorText);
public readonly record struct AccountSnapshot(string AccountId, string AccountName, ulong CharacterId, string CreatedAtUtc, string? LastLoginAtUtc);
