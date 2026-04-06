using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class InventoryStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public InventoryStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public InventorySnapshot GetInventory(ulong characterId, int capacity = 40)
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
            command.CommandText =
                "SELECT slot_index, item_id, quantity FROM inventory_slots WHERE character_id = $character ORDER BY slot_index;";
            command.Parameters.AddWithValue("$character", unchecked((long)characterId));

            using var reader = command.ExecuteReader();
            var slots = new List<InventorySlotSnapshot>();
            while (reader.Read())
            {
                slots.Add(new InventorySlotSnapshot(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
            }

            return new InventorySnapshot(characterId, capacity, slots.ToArray());
        }
    }

    public InventorySnapshot AddItem(ulong characterId, string itemId, int quantity, int maxStack, int capacity = 40)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("quantity must be > 0.");
        }

        if (maxStack <= 0)
        {
            throw new InvalidOperationException("maxStack must be > 0.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            var slots = ReadSlots(connection, tx, characterId);
            var remaining = quantity;

            foreach (var slot in slots.Where(slot => string.Equals(slot.ItemId, itemId, StringComparison.OrdinalIgnoreCase) && slot.Quantity < maxStack))
            {
                var room = maxStack - slot.Quantity;
                var add = Math.Min(room, remaining);
                slot.Quantity += add;
                remaining -= add;
                if (remaining <= 0)
                {
                    break;
                }
            }

            while (remaining > 0)
            {
                var freeSlot = Enumerable.Range(0, capacity)
                    .Select(index => (int?)index)
                    .FirstOrDefault(index => slots.All(slot => slot.SlotIndex != index));

                if (!freeSlot.HasValue)
                {
                    throw new InvalidOperationException("Inventory is full.");
                }

                var add = Math.Min(maxStack, remaining);
                slots.Add(new MutableSlot(freeSlot.Value, itemId, add));
                remaining -= add;
            }

            PersistSlots(connection, tx, characterId, slots);
            tx.Commit();
        }

        return GetInventory(characterId, capacity);
    }

    public InventorySnapshot Move(ulong characterId, int fromSlot, int toSlot, int capacity = 40)
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var slots = ReadSlots(connection, tx, characterId);

            var source = slots.FirstOrDefault(slot => slot.SlotIndex == fromSlot)
                         ?? throw new InvalidOperationException("Source slot is empty.");
            var target = slots.FirstOrDefault(slot => slot.SlotIndex == toSlot);

            if (target is null)
            {
                source.SlotIndex = toSlot;
            }
            else
            {
                (source.ItemId, target.ItemId) = (target.ItemId, source.ItemId);
                (source.Quantity, target.Quantity) = (target.Quantity, source.Quantity);
            }

            PersistSlots(connection, tx, characterId, slots);
            tx.Commit();
        }

        return GetInventory(characterId, capacity);
    }

    public InventorySnapshot Split(ulong characterId, int fromSlot, int toSlot, int quantity, int capacity = 40)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("quantity must be > 0.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var slots = ReadSlots(connection, tx, characterId);

            var source = slots.FirstOrDefault(slot => slot.SlotIndex == fromSlot)
                         ?? throw new InvalidOperationException("Source slot is empty.");

            if (source.Quantity <= quantity)
            {
                throw new InvalidOperationException("Not enough quantity to split.");
            }

            if (slots.Any(slot => slot.SlotIndex == toSlot))
            {
                throw new InvalidOperationException("Target slot is already occupied.");
            }

            source.Quantity -= quantity;
            slots.Add(new MutableSlot(toSlot, source.ItemId, quantity));

            PersistSlots(connection, tx, characterId, slots);
            tx.Commit();
        }

        return GetInventory(characterId, capacity);
    }

    public InventorySnapshot Remove(ulong characterId, int slotIndex, int quantity, int capacity = 40)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("quantity must be > 0.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var slots = ReadSlots(connection, tx, characterId);

            var slot = slots.FirstOrDefault(entry => entry.SlotIndex == slotIndex)
                       ?? throw new InvalidOperationException("Slot is empty.");

            slot.Quantity -= quantity;
            if (slot.Quantity <= 0)
            {
                slots.Remove(slot);
            }

            PersistSlots(connection, tx, characterId, slots);
            tx.Commit();
        }

        return GetInventory(characterId, capacity);
    }

    public InventorySnapshot Craft(ulong characterId, CraftingRecipeSnapshot recipe, int outputMaxStack, int capacity = 40)
    {
        if (recipe.Ingredients.Length == 0)
        {
            throw new InvalidOperationException("Recipe requires at least one ingredient.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var slots = ReadSlots(connection, tx, characterId);

            foreach (var ingredient in recipe.Ingredients)
            {
                var total = slots
                    .Where(slot => string.Equals(slot.ItemId, ingredient.ItemId, StringComparison.OrdinalIgnoreCase))
                    .Sum(slot => slot.Quantity);

                if (total < ingredient.Quantity)
                {
                    throw new InvalidOperationException($"Missing ingredient {ingredient.ItemId}. Need {ingredient.Quantity}, have {total}.");
                }
            }

            foreach (var ingredient in recipe.Ingredients)
            {
                var remaining = ingredient.Quantity;
                foreach (var slot in slots.Where(slot => string.Equals(slot.ItemId, ingredient.ItemId, StringComparison.OrdinalIgnoreCase)).OrderBy(slot => slot.SlotIndex).ToList())
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    var consume = Math.Min(remaining, slot.Quantity);
                    slot.Quantity -= consume;
                    remaining -= consume;
                    if (slot.Quantity <= 0)
                    {
                        slots.Remove(slot);
                    }
                }
            }

            var outputRemaining = recipe.OutputQuantity;
            foreach (var slot in slots.Where(slot => string.Equals(slot.ItemId, recipe.OutputItemId, StringComparison.OrdinalIgnoreCase) && slot.Quantity < outputMaxStack))
            {
                var room = outputMaxStack - slot.Quantity;
                var add = Math.Min(room, outputRemaining);
                slot.Quantity += add;
                outputRemaining -= add;
                if (outputRemaining <= 0)
                {
                    break;
                }
            }

            while (outputRemaining > 0)
            {
                var freeSlot = Enumerable.Range(0, capacity)
                    .Select(index => (int?)index)
                    .FirstOrDefault(index => slots.All(slot => slot.SlotIndex != index));

                if (!freeSlot.HasValue)
                {
                    throw new InvalidOperationException("Inventory is full for crafting output.");
                }

                var add = Math.Min(outputMaxStack, outputRemaining);
                slots.Add(new MutableSlot(freeSlot.Value, recipe.OutputItemId, add));
                outputRemaining -= add;
            }

            PersistSlots(connection, tx, characterId, slots);
            tx.Commit();
        }

        return GetInventory(characterId, capacity);
    }

    public InventorySnapshot Exchange(ulong characterId, string priceItemId, int priceQuantity, string rewardItemId, int rewardQuantity, int rewardMaxStack, int capacity = 40)
    {
        if (string.IsNullOrWhiteSpace(priceItemId) || priceQuantity <= 0)
        {
            throw new InvalidOperationException("Valid price item and quantity are required.");
        }

        if (string.IsNullOrWhiteSpace(rewardItemId) || rewardQuantity <= 0 || rewardMaxStack <= 0)
        {
            throw new InvalidOperationException("Valid reward item, quantity, and max stack are required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            var slots = ReadSlots(connection, tx, characterId);

            var total = slots
                .Where(slot => string.Equals(slot.ItemId, priceItemId, StringComparison.OrdinalIgnoreCase))
                .Sum(slot => slot.Quantity);
            if (total < priceQuantity)
            {
                throw new InvalidOperationException($"Missing payment item {priceItemId}. Need {priceQuantity}, have {total}.");
            }

            var remainingPrice = priceQuantity;
            foreach (var slot in slots.Where(slot => string.Equals(slot.ItemId, priceItemId, StringComparison.OrdinalIgnoreCase)).OrderBy(slot => slot.SlotIndex).ToList())
            {
                if (remainingPrice <= 0)
                {
                    break;
                }

                var consume = Math.Min(remainingPrice, slot.Quantity);
                slot.Quantity -= consume;
                remainingPrice -= consume;
                if (slot.Quantity <= 0)
                {
                    slots.Remove(slot);
                }
            }

            var outputRemaining = rewardQuantity;
            foreach (var slot in slots.Where(slot => string.Equals(slot.ItemId, rewardItemId, StringComparison.OrdinalIgnoreCase) && slot.Quantity < rewardMaxStack))
            {
                var room = rewardMaxStack - slot.Quantity;
                var add = Math.Min(room, outputRemaining);
                slot.Quantity += add;
                outputRemaining -= add;
                if (outputRemaining <= 0)
                {
                    break;
                }
            }

            while (outputRemaining > 0)
            {
                var freeSlot = Enumerable.Range(0, capacity)
                    .Select(index => (int?)index)
                    .FirstOrDefault(index => slots.All(slot => slot.SlotIndex != index));

                if (!freeSlot.HasValue)
                {
                    throw new InvalidOperationException("Inventory is full for shop purchase.");
                }

                var add = Math.Min(rewardMaxStack, outputRemaining);
                slots.Add(new MutableSlot(freeSlot.Value, rewardItemId, add));
                outputRemaining -= add;
            }

            PersistSlots(connection, tx, characterId, slots);
            tx.Commit();
        }

        return GetInventory(characterId, capacity);
    }

    private static List<MutableSlot> ReadSlots(SqliteConnection connection, SqliteTransaction tx, ulong characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            "SELECT slot_index, item_id, quantity FROM inventory_slots WHERE character_id = $character ORDER BY slot_index;";
        command.Parameters.AddWithValue("$character", unchecked((long)characterId));

        using var reader = command.ExecuteReader();
        var slots = new List<MutableSlot>();
        while (reader.Read())
        {
            slots.Add(new MutableSlot(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return slots;
    }

    private static void PersistSlots(SqliteConnection connection, SqliteTransaction tx, ulong characterId, IReadOnlyCollection<MutableSlot> slots)
    {
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM inventory_slots WHERE character_id = $character;";
            clear.Parameters.AddWithValue("$character", unchecked((long)characterId));
            clear.ExecuteNonQuery();
        }

        foreach (var slot in slots.OrderBy(slot => slot.SlotIndex))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO inventory_slots(character_id, slot_index, item_id, quantity, updated_at_utc) VALUES ($character,$slot,$item,$qty,$updated);";
            insert.Parameters.AddWithValue("$character", unchecked((long)characterId));
            insert.Parameters.AddWithValue("$slot", slot.SlotIndex);
            insert.Parameters.AddWithValue("$item", slot.ItemId);
            insert.Parameters.AddWithValue("$qty", slot.Quantity);
            insert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        if (!TableExists(connection, "inventory_slots"))
        {
            CreateCharacterScopedInventoryTable(connection, "inventory_slots");
            return;
        }

        if (ColumnExists(connection, "inventory_slots", "account_id"))
        {
            MigrateLegacyAccountScopedTable(connection);
        }

        using var index = connection.CreateCommand();
        index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_slots_character_slot ON inventory_slots(character_id, slot_index);";
        index.ExecuteNonQuery();
    }

    private static void MigrateLegacyAccountScopedTable(SqliteConnection connection)
    {
        CreateCharacterScopedInventoryTable(connection, "inventory_slots_v2");

        using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText =
                """
                INSERT OR REPLACE INTO inventory_slots_v2(character_id, slot_index, item_id, quantity, updated_at_utc)
                SELECT resolved.character_id, resolved.slot_index, resolved.item_id, resolved.quantity, resolved.updated_at_utc
                FROM (
                    SELECT
                        CASE
                            WHEN inventory_slots.character_id IS NOT NULL THEN inventory_slots.character_id
                            ELSE (
                                SELECT primary_character_id
                                FROM accounts
                                WHERE accounts.account_id = inventory_slots.account_id
                            )
                        END AS character_id,
                        inventory_slots.slot_index,
                        inventory_slots.item_id,
                        inventory_slots.quantity,
                        inventory_slots.updated_at_utc
                    FROM inventory_slots
                ) AS resolved
                WHERE resolved.character_id IS NOT NULL;
                """;
            migrate.ExecuteNonQuery();
        }

        using (var dropLegacy = connection.CreateCommand())
        {
            dropLegacy.CommandText = "DROP TABLE inventory_slots;";
            dropLegacy.ExecuteNonQuery();
        }

        using (var rename = connection.CreateCommand())
        {
            rename.CommandText = "ALTER TABLE inventory_slots_v2 RENAME TO inventory_slots;";
            rename.ExecuteNonQuery();
        }
    }

    private static void CreateCharacterScopedInventoryTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                character_id INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_id TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (character_id, slot_index)
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

    private sealed class MutableSlot
    {
        public MutableSlot(int slotIndex, string itemId, int quantity)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = quantity;
        }

        public int SlotIndex { get; set; }
        public string ItemId { get; set; }
        public int Quantity { get; set; }
    }
}
