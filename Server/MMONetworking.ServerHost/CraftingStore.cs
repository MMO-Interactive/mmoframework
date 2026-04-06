using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MMONetworking.ServerHost;

public sealed class CraftingStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public CraftingStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
        SeedDefaultsIfEmpty();
    }

    public CraftingRecipeSnapshot[] GetRecipes()
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT recipe_id, name, output_item_id, output_qty, craft_seconds, ingredients_json, provider_npc_id FROM crafting_recipes ORDER BY recipe_id;";
            using var reader = command.ExecuteReader();
            var recipes = new List<CraftingRecipeSnapshot>();
            while (reader.Read())
            {
                recipes.Add(new CraftingRecipeSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    JsonSerializer.Deserialize<CraftingIngredientSnapshot[]>(reader.GetString(5), JsonOptions) ?? Array.Empty<CraftingIngredientSnapshot>(),
                    reader.GetString(6)));
            }

            return recipes.ToArray();
        }
    }

    public CraftingRecipeSnapshot UpsertRecipe(CraftingRecipeSnapshot recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe.RecipeId))
        {
            throw new InvalidOperationException("recipeId is required.");
        }

        if (string.IsNullOrWhiteSpace(recipe.OutputItemId) || recipe.OutputQuantity <= 0)
        {
            throw new InvalidOperationException("Recipe output must be valid.");
        }

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO crafting_recipes(recipe_id, name, output_item_id, output_qty, craft_seconds, ingredients_json, provider_npc_id, updated_at_utc)
                VALUES ($id, $name, $outputItem, $outputQty, $seconds, $ingredients, $providerNpcId, $updated)
                ON CONFLICT(recipe_id) DO UPDATE SET
                    name = excluded.name,
                    output_item_id = excluded.output_item_id,
                    output_qty = excluded.output_qty,
                    craft_seconds = excluded.craft_seconds,
                    ingredients_json = excluded.ingredients_json,
                    provider_npc_id = excluded.provider_npc_id,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", recipe.RecipeId.Trim());
            command.Parameters.AddWithValue("$name", recipe.Name ?? recipe.RecipeId);
            command.Parameters.AddWithValue("$outputItem", recipe.OutputItemId);
            command.Parameters.AddWithValue("$outputQty", recipe.OutputQuantity);
            command.Parameters.AddWithValue("$seconds", recipe.CraftSeconds);
            command.Parameters.AddWithValue("$ingredients", JsonSerializer.Serialize(recipe.Ingredients ?? Array.Empty<CraftingIngredientSnapshot>(), JsonOptions));
            command.Parameters.AddWithValue("$providerNpcId", recipe.ProviderNpcId ?? string.Empty);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        return recipe;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS crafting_recipes (
                recipe_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                output_item_id TEXT NOT NULL,
                output_qty INTEGER NOT NULL,
                craft_seconds INTEGER NOT NULL,
                ingredients_json TEXT NOT NULL,
                provider_npc_id TEXT NOT NULL DEFAULT '',
                updated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE crafting_recipes ADD COLUMN provider_npc_id TEXT NOT NULL DEFAULT '';";
        try
        {
            alterCommand.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
        }
    }

    private void SeedDefaultsIfEmpty()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM crafting_recipes;";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        UpsertRecipe(new CraftingRecipeSnapshot(
            "make-planks",
            "Make Planks",
            "plank",
            2,
            3,
            new[] { new CraftingIngredientSnapshot("log", 1) },
            "merchant-1"));

        UpsertRecipe(new CraftingRecipeSnapshot(
            "smelt-ingot",
            "Smelt Ingot",
            "ingot",
            1,
            5,
            new[] { new CraftingIngredientSnapshot("ore", 2) },
            "merchant-1"));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
