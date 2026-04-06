using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class ShopStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public ShopStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
        SeedDefaultsIfEmpty();
    }

    public ShopCatalogSnapshot GetCatalog(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            throw new InvalidOperationException("npcId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT offer_id, npc_id, item_id, item_name, quantity, price_item_id, price_item_quantity
                FROM shop_offers
                WHERE npc_id = $npcId
                ORDER BY offer_id;
                """;
            command.Parameters.AddWithValue("$npcId", npcId.Trim());

            using var reader = command.ExecuteReader();
            var offers = new List<ShopOfferSnapshot>();
            while (reader.Read())
            {
                offers.Add(new ShopOfferSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetInt32(6)));
            }

            return new ShopCatalogSnapshot(npcId.Trim(), offers.ToArray());
        }
    }

    public ShopOfferSnapshot GetOffer(string offerId)
    {
        if (string.IsNullOrWhiteSpace(offerId))
        {
            throw new InvalidOperationException("offerId is required.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT offer_id, npc_id, item_id, item_name, quantity, price_item_id, price_item_quantity
                FROM shop_offers
                WHERE offer_id = $offerId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$offerId", offerId.Trim());
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Shop offer not found.");
            }

            return new ShopOfferSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6));
        }
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS shop_offers (
                offer_id TEXT PRIMARY KEY,
                npc_id TEXT NOT NULL,
                item_id TEXT NOT NULL,
                item_name TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                price_item_id TEXT NOT NULL,
                price_item_quantity INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private void SeedDefaultsIfEmpty()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM shop_offers;";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        UpsertOffer(new ShopOfferSnapshot("merchant-1-bandage", "merchant-1", "bandage", "Field Bandage", 1, "log", 1));
        UpsertOffer(new ShopOfferSnapshot("merchant-1-ration", "merchant-1", "ration", "Trail Ration", 1, "ore", 1));
        UpsertOffer(new ShopOfferSnapshot("merchant-1-kit", "merchant-1", "camp_kit", "Camp Kit", 1, "log", 3));
    }

    private void UpsertOffer(ShopOfferSnapshot offer)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO shop_offers(offer_id, npc_id, item_id, item_name, quantity, price_item_id, price_item_quantity, updated_at_utc)
            VALUES ($offerId, $npcId, $itemId, $itemName, $quantity, $priceItemId, $priceItemQuantity, $updatedAtUtc)
            ON CONFLICT(offer_id) DO UPDATE SET
                npc_id = excluded.npc_id,
                item_id = excluded.item_id,
                item_name = excluded.item_name,
                quantity = excluded.quantity,
                price_item_id = excluded.price_item_id,
                price_item_quantity = excluded.price_item_quantity,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$offerId", offer.OfferId);
        command.Parameters.AddWithValue("$npcId", offer.NpcId);
        command.Parameters.AddWithValue("$itemId", offer.ItemId);
        command.Parameters.AddWithValue("$itemName", offer.ItemName);
        command.Parameters.AddWithValue("$quantity", offer.Quantity);
        command.Parameters.AddWithValue("$priceItemId", offer.PriceItemId);
        command.Parameters.AddWithValue("$priceItemQuantity", offer.PriceItemQuantity);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
