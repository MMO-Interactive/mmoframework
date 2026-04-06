using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class GameplayDefinitionStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public GameplayDefinitionStore(string databasePath, ZoneDirectory zoneDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
        SeedDefaultsIfEmpty(zoneDirectory);
        EnsureExpandedSkillCatalog();
    }

    public GameplayDefinitionsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return ReadSnapshot(connection);
        }
    }

    public NpcDefinitionSnapshot[] GetNpcsForZone(int zoneId)
        => GetSnapshot()
            .Npcs
            .Where(npc => npc.ZoneId == zoneId)
            .OrderBy(npc => npc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(npc => npc.NpcId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public PlayerSpawnDefinitionSnapshot[] GetPlayerSpawnsForZone(int zoneId)
        => GetSnapshot()
            .PlayerSpawns
            .Where(spawn => spawn.ZoneId == zoneId)
            .OrderByDescending(spawn => spawn.IsDefaultSpawn)
            .ThenBy(spawn => spawn.SpawnTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(spawn => spawn.SpawnId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public MobSpawnDefinitionSnapshot[] GetMobSpawnsForZone(int zoneId)
        => GetSnapshot()
            .MobSpawns
            .Where(spawn => spawn.ZoneId == zoneId)
            .OrderBy(spawn => spawn.MobTypeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(spawn => spawn.SpawnId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public NetworkVector3 GetPreferredPlayerSpawn(int zoneId, ZoneDefinition zone)
    {
        var spawn = GetPlayerSpawnsForZone(zoneId).FirstOrDefault(entry => entry.IsDefaultSpawn)
            ?? GetPlayerSpawnsForZone(zoneId).FirstOrDefault();
        if (spawn is null)
        {
            return new NetworkVector3(zone.MinX + 5f, 0f, zone.MinZ + 5f);
        }

        return zone.Clamp(new NetworkVector3(spawn.PositionX, spawn.PositionY, spawn.PositionZ));
    }

    public GameplayDefinitionsSnapshot Update(GameplayDefinitionsSnapshot next)
    {
        next = new GameplayDefinitionsSnapshot(
            next.Items ?? Array.Empty<ItemDefinitionSnapshot>(),
            next.Skills ?? Array.Empty<SkillDefinitionSnapshot>(),
            next.Resources ?? Array.Empty<ResourceDefinitionSnapshot>(),
            next.Nodes ?? Array.Empty<ResourceNodeDefinitionSnapshot>(),
            next.Zones ?? Array.Empty<ZoneDefinitionSnapshot>(),
            next.Npcs ?? Array.Empty<NpcDefinitionSnapshot>(),
            next.PlayerSpawns ?? Array.Empty<PlayerSpawnDefinitionSnapshot>(),
            next.MobSpawns ?? Array.Empty<MobSpawnDefinitionSnapshot>());

        Validate(next);

        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            UpsertSection(connection, transaction, "items", next.Items);
            UpsertSection(connection, transaction, "skills", next.Skills);
            UpsertSection(connection, transaction, "resources", next.Resources);
            UpsertSection(connection, transaction, "nodes", next.Nodes);
            UpsertSection(connection, transaction, "zones", next.Zones);
            UpsertSection(connection, transaction, "npcs", next.Npcs);
            UpsertSection(connection, transaction, "playerSpawns", next.PlayerSpawns);
            UpsertSection(connection, transaction, "mobSpawns", next.MobSpawns);
            transaction.Commit();
            return ReadSnapshot(connection);
        }
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS gameplay_definitions (
                section TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private void SeedDefaultsIfEmpty(ZoneDirectory zoneDirectory)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM gameplay_definitions;";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        var defaults = new GameplayDefinitionsSnapshot(
            Items: new[]
            {
                new ItemDefinitionSnapshot("log", "Wood Log", 200, 1.0f),
                new ItemDefinitionSnapshot("ore", "Iron Ore", 200, 1.5f)
            },
            Skills: BuildWurmSkillDefaults(),
            Resources: new[]
            {
                new ResourceDefinitionSnapshot("tree", "Tree", "log", 1),
                new ResourceDefinitionSnapshot("ore_vein", "Ore Vein", "ore", 1)
            },
            Nodes: new[]
            {
                new ResourceNodeDefinitionSnapshot("tree-1", 1, "tree", 10f, 0f, 45f, 8),
                new ResourceNodeDefinitionSnapshot("ore-1", 1, "ore_vein", 15f, 0f, 55f, 8)
            },
            Zones: zoneDirectory.All
                .OrderBy(zone => zone.ZoneId)
                .Select(zone => new ZoneDefinitionSnapshot(zone.ZoneId, zone.Name, string.IsNullOrWhiteSpace(zone.AssetBundleName) ? "zone-" + zone.ZoneId : zone.AssetBundleName, zone.MinX, zone.MaxX, zone.MinZ, zone.MaxZ))
                .ToArray(),
            Npcs: zoneDirectory.All
                .OrderBy(zone => zone.ZoneId)
                .SelectMany(BuildDefaultNpcsForZone)
                .ToArray(),
            PlayerSpawns: zoneDirectory.All
                .OrderBy(zone => zone.ZoneId)
                .SelectMany(BuildDefaultPlayerSpawnsForZone)
                .ToArray(),
            MobSpawns: zoneDirectory.All
                .OrderBy(zone => zone.ZoneId)
                .SelectMany(BuildDefaultMobSpawnsForZone)
                .ToArray());

        using var transaction = connection.BeginTransaction();
        UpsertSection(connection, transaction, "items", defaults.Items);
        UpsertSection(connection, transaction, "skills", defaults.Skills);
        UpsertSection(connection, transaction, "resources", defaults.Resources);
        UpsertSection(connection, transaction, "nodes", defaults.Nodes);
        UpsertSection(connection, transaction, "zones", defaults.Zones);
        UpsertSection(connection, transaction, "npcs", defaults.Npcs);
        UpsertSection(connection, transaction, "playerSpawns", defaults.PlayerSpawns);
        UpsertSection(connection, transaction, "mobSpawns", defaults.MobSpawns);
        transaction.Commit();
    }

    private void EnsureExpandedSkillCatalog()
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var existingSkills = ReadSection<SkillDefinitionSnapshot>(connection, "skills");
            if (existingSkills.Length == 0)
            {
                return;
            }

            var defaultSkills = BuildWurmSkillDefaults();
            var merged = existingSkills
                .Concat(defaultSkills)
                .GroupBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (merged.Length == existingSkills.Length)
            {
                return;
            }

            using var transaction = connection.BeginTransaction();
            UpsertSection(connection, transaction, "skills", merged);
            transaction.Commit();
        }
    }

    private static SkillDefinitionSnapshot[] BuildWurmSkillDefaults()
        => new[]
        {
            new SkillDefinitionSnapshot("alchemy", "Alchemy", 100),
            new SkillDefinitionSnapshot("natural_substances", "Natural Substances", 100),
            new SkillDefinitionSnapshot("archery", "Archery", 100),
            new SkillDefinitionSnapshot("short_bow", "Short Bow", 100),
            new SkillDefinitionSnapshot("medium_bow", "Medium Bow", 100),
            new SkillDefinitionSnapshot("long_bow", "Long Bow", 100),
            new SkillDefinitionSnapshot("axes", "Axes", 100),
            new SkillDefinitionSnapshot("small_axe", "Small Axe", 100),
            new SkillDefinitionSnapshot("hatchet", "Hatchet", 100),
            new SkillDefinitionSnapshot("large_axe", "Large Axe", 100),
            new SkillDefinitionSnapshot("huge_axe", "Huge Axe", 100),
            new SkillDefinitionSnapshot("carpentry", "Carpentry", 100),
            new SkillDefinitionSnapshot("bowyery", "Bowyery", 100),
            new SkillDefinitionSnapshot("fletching", "Fletching", 100),
            new SkillDefinitionSnapshot("fine_carpentry", "Fine Carpentry", 100),
            new SkillDefinitionSnapshot("toy_making", "Toy Making", 100),
            new SkillDefinitionSnapshot("ship_building", "Ship Building", 100),
            new SkillDefinitionSnapshot("climbing", "Climbing", 100),
            new SkillDefinitionSnapshot("clubs", "Clubs", 100),
            new SkillDefinitionSnapshot("huge_club", "Huge Club", 100),
            new SkillDefinitionSnapshot("coal_making", "Coal-making", 100),
            new SkillDefinitionSnapshot("cooking", "Cooking", 100),
            new SkillDefinitionSnapshot("hot_food_cooking", "Hot Food Cooking", 100),
            new SkillDefinitionSnapshot("baking", "Baking", 100),
            new SkillDefinitionSnapshot("dairy_food_making", "Dairy Food Making", 100),
            new SkillDefinitionSnapshot("butchering", "Butchering", 100),
            new SkillDefinitionSnapshot("beverages", "Beverages", 100),
            new SkillDefinitionSnapshot("digging", "Digging", 100),
            new SkillDefinitionSnapshot("fighting", "Fighting", 100),
            new SkillDefinitionSnapshot("weaponless_fighting", "Weaponless Fighting", 100),
            new SkillDefinitionSnapshot("aggressive_fighting", "Aggressive Fighting", 100),
            new SkillDefinitionSnapshot("normal_fighting", "Normal Fighting", 100),
            new SkillDefinitionSnapshot("defensive_fighting", "Defensive Fighting", 100),
            new SkillDefinitionSnapshot("taunting", "Taunting", 100),
            new SkillDefinitionSnapshot("shield_bashing", "Shield Bashing", 100),
            new SkillDefinitionSnapshot("firemaking", "Firemaking", 100),
            new SkillDefinitionSnapshot("hammers", "Hammers", 100),
            new SkillDefinitionSnapshot("warhammer", "Warhammer", 100),
            new SkillDefinitionSnapshot("healing", "Healing", 100),
            new SkillDefinitionSnapshot("first_aid", "First Aid", 100),
            new SkillDefinitionSnapshot("knives", "Knives", 100),
            new SkillDefinitionSnapshot("carving_knife", "Carving Knife", 100),
            new SkillDefinitionSnapshot("butchering_knife", "Butchering Knife", 100),
            new SkillDefinitionSnapshot("masonry", "Masonry", 100),
            new SkillDefinitionSnapshot("stone_cutting", "Stone Cutting", 100),
            new SkillDefinitionSnapshot("mauls", "Mauls", 100),
            new SkillDefinitionSnapshot("small_maul", "Small Maul", 100),
            new SkillDefinitionSnapshot("medium_maul", "Medium Maul", 100),
            new SkillDefinitionSnapshot("large_maul", "Large Maul", 100),
            new SkillDefinitionSnapshot("milling", "Milling", 100),
            new SkillDefinitionSnapshot("mining", "Mining", 100),
            new SkillDefinitionSnapshot("paving", "Paving", 100),
            new SkillDefinitionSnapshot("pottery", "Pottery", 100),
            new SkillDefinitionSnapshot("prospecting", "Prospecting", 100),
            new SkillDefinitionSnapshot("ropemaking", "Ropemaking", 100),
            new SkillDefinitionSnapshot("tracking", "Tracking", 100),
            new SkillDefinitionSnapshot("woodcutting", "Woodcutting", 100),
            new SkillDefinitionSnapshot("nature", "Nature", 100),
            new SkillDefinitionSnapshot("fishing", "Fishing", 100),
            new SkillDefinitionSnapshot("farming", "Farming", 100),
            new SkillDefinitionSnapshot("forestry", "Forestry", 100),
            new SkillDefinitionSnapshot("milking", "Milking", 100),
            new SkillDefinitionSnapshot("foraging", "Foraging", 100),
            new SkillDefinitionSnapshot("botanizing", "Botanizing", 100),
            new SkillDefinitionSnapshot("gardening", "Gardening", 100),
            new SkillDefinitionSnapshot("animal_taming", "Animal Taming", 100),
            new SkillDefinitionSnapshot("animal_husbandry", "Animal Husbandry", 100),
            new SkillDefinitionSnapshot("meditating", "Meditating", 100),
            new SkillDefinitionSnapshot("polearms", "Polearms", 100),
            new SkillDefinitionSnapshot("halberd", "Halberd", 100),
            new SkillDefinitionSnapshot("long_spear", "Long Spear", 100),
            new SkillDefinitionSnapshot("staff", "Staff", 100),
            new SkillDefinitionSnapshot("religion", "Religion", 100),
            new SkillDefinitionSnapshot("preaching", "Preaching", 100),
            new SkillDefinitionSnapshot("prayer", "Prayer", 100),
            new SkillDefinitionSnapshot("channeling", "Channeling", 100),
            new SkillDefinitionSnapshot("exorcism", "Exorcism", 100),
            new SkillDefinitionSnapshot("shields", "Shields", 100),
            new SkillDefinitionSnapshot("small_wooden_shield", "Small Wooden Shield", 100),
            new SkillDefinitionSnapshot("medium_wooden_shield", "Medium Wooden Shield", 100),
            new SkillDefinitionSnapshot("large_wooden_shield", "Large Wooden Shield", 100),
            new SkillDefinitionSnapshot("small_metal_shield", "Small Metal Shield", 100),
            new SkillDefinitionSnapshot("medium_metal_shield", "Medium Metal Shield", 100),
            new SkillDefinitionSnapshot("large_metal_shield", "Large Metal Shield", 100),
            new SkillDefinitionSnapshot("smithing", "Smithing", 100),
            new SkillDefinitionSnapshot("weapon_smithing", "Weapon Smithing", 100),
            new SkillDefinitionSnapshot("blades_smithing", "Blades Smithing", 100),
            new SkillDefinitionSnapshot("weapon_heads_smithing", "Weapon Heads Smithing", 100),
            new SkillDefinitionSnapshot("armour_smithing", "Armour Smithing", 100),
            new SkillDefinitionSnapshot("chain_armour_smithing", "Chain Armour Smithing", 100),
            new SkillDefinitionSnapshot("plate_armour_smithing", "Plate Armour Smithing", 100),
            new SkillDefinitionSnapshot("shield_smithing", "Shield Smithing", 100),
            new SkillDefinitionSnapshot("blacksmithing", "Blacksmithing", 100),
            new SkillDefinitionSnapshot("locksmithing", "Locksmithing", 100),
            new SkillDefinitionSnapshot("metallurgy", "Metallurgy", 100),
            new SkillDefinitionSnapshot("jewelry_smithing", "Jewelry Smithing", 100),
            new SkillDefinitionSnapshot("swords", "Swords", 100),
            new SkillDefinitionSnapshot("short_sword", "Short Sword", 100),
            new SkillDefinitionSnapshot("long_sword", "Long Sword", 100),
            new SkillDefinitionSnapshot("two_handed_sword", "Two Handed Sword", 100),
            new SkillDefinitionSnapshot("tailoring", "Tailoring", 100),
            new SkillDefinitionSnapshot("cloth_tailoring", "Cloth Tailoring", 100),
            new SkillDefinitionSnapshot("leatherworking", "Leatherworking", 100),
            new SkillDefinitionSnapshot("thievery", "Thievery", 100),
            new SkillDefinitionSnapshot("stealing", "Stealing", 100),
            new SkillDefinitionSnapshot("lockpicking", "Lockpicking", 100),
            new SkillDefinitionSnapshot("traps", "Traps", 100),
            new SkillDefinitionSnapshot("toys", "Toys", 100),
            new SkillDefinitionSnapshot("yoyo", "Yoyo", 100),
            new SkillDefinitionSnapshot("puppeteering", "Puppeteering", 100),
            new SkillDefinitionSnapshot("war_machines", "War Machines", 100),
            new SkillDefinitionSnapshot("catapults", "Catapults", 100),
            new SkillDefinitionSnapshot("miscellaneous_items", "Miscellaneous Items", 100),
            new SkillDefinitionSnapshot("shovel", "Shovel", 100),
            new SkillDefinitionSnapshot("rake", "Rake", 100),
            new SkillDefinitionSnapshot("hammer", "Hammer", 100),
            new SkillDefinitionSnapshot("saw", "Saw", 100),
            new SkillDefinitionSnapshot("sickle", "Sickle", 100),
            new SkillDefinitionSnapshot("scythe", "Scythe", 100),
            new SkillDefinitionSnapshot("repairing", "Repairing", 100),
            new SkillDefinitionSnapshot("pickaxe", "Pickaxe", 100),
            new SkillDefinitionSnapshot("stone_chisel", "Stone Chisel", 100)
        };

    private GameplayDefinitionsSnapshot ReadSnapshot(SqliteConnection connection)
        => new(
            Items: ReadSection<ItemDefinitionSnapshot>(connection, "items"),
            Skills: ReadSection<SkillDefinitionSnapshot>(connection, "skills"),
            Resources: ReadSection<ResourceDefinitionSnapshot>(connection, "resources"),
            Nodes: ReadSection<ResourceNodeDefinitionSnapshot>(connection, "nodes"),
            Zones: ReadSection<ZoneDefinitionSnapshot>(connection, "zones"),
            Npcs: ReadSection<NpcDefinitionSnapshot>(connection, "npcs"),
            PlayerSpawns: ReadSection<PlayerSpawnDefinitionSnapshot>(connection, "playerSpawns"),
            MobSpawns: ReadSection<MobSpawnDefinitionSnapshot>(connection, "mobSpawns"));

    private static NpcDefinitionSnapshot[] BuildDefaultNpcsForZone(ZoneDefinition zone)
    {
        var midZ = (zone.MinZ + zone.MaxZ) * 0.5f;
        return new[]
        {
            new NpcDefinitionSnapshot(
                "merchant-" + zone.ZoneId,
                zone.ZoneId,
                "merchant",
                "Quartermaster Rowan",
                zone.MinX + 22f,
                0f,
                midZ - 3f,
                "shop",
                new[] { "shop", "crafting" },
                "Supplies for the road, tools for the trade, and a fair barter if your pack is worth opening.",
                BuildDefaultServiceOptions("shop", "crafting")),
            new NpcDefinitionSnapshot(
                "questgiver-" + zone.ZoneId,
                zone.ZoneId,
                "quest_giver",
                "Warden Elira",
                zone.MinX + 28f,
                0f,
                midZ + 3f,
                "quest",
                new[] { "quests" },
                "Every frontier needs hands willing to work. If you want purpose, I have tasks that matter.",
                BuildDefaultServiceOptions("quests")),
            new NpcDefinitionSnapshot(
                "trainer-" + zone.ZoneId,
                zone.ZoneId,
                "trainer",
                "Master Toren",
                zone.MinX + 34f,
                0f,
                midZ,
                "trainer",
                new[] { "training", "progression" },
                "Skill is earned, not granted. Show me what you've practiced, and I'll show you where to sharpen it next.",
                BuildDefaultServiceOptions("training"))
        };
    }

    private static PlayerSpawnDefinitionSnapshot[] BuildDefaultPlayerSpawnsForZone(ZoneDefinition zone)
    {
        var spawnX = zone.MinX + 5f;
        var spawnZ = zone.MinZ + 5f;
        return new[]
        {
            new PlayerSpawnDefinitionSnapshot(
                "spawn-" + zone.ZoneId + "-starter",
                zone.ZoneId,
                spawnX,
                0f,
                spawnZ,
                true,
                0f,
                "starter")
        };
    }

    private static MobSpawnDefinitionSnapshot[] BuildDefaultMobSpawnsForZone(ZoneDefinition zone)
    {
        var centerX = (zone.MinX + zone.MaxX) * 0.5f;
        var centerZ = (zone.MinZ + zone.MaxZ) * 0.5f;
        var baseCount = zone.ZoneId == 1 ? 100 : 2;
        return new[]
        {
            new MobSpawnDefinitionSnapshot(
                "mob-" + zone.ZoneId + "-pack-a",
                zone.ZoneId,
                "wolf",
                centerX - 10f,
                0f,
                centerZ - 8f,
                Math.Max(1, baseCount / 2),
                10f,
                16f),
            new MobSpawnDefinitionSnapshot(
                "mob-" + zone.ZoneId + "-pack-b",
                zone.ZoneId,
                "boar",
                centerX + 12f,
                0f,
                centerZ + 6f,
                Math.Max(1, baseCount - Math.Max(1, baseCount / 2)),
                9f,
                15f)
        };
    }

    private static NpcServiceDefinitionSnapshot[] BuildDefaultServiceOptions(params string[] actions)
        => actions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(action => action.Trim().ToLowerInvariant())
            .Where(action => action.Length > 0)
            .Select(action => action switch
            {
                "shop" => new NpcServiceDefinitionSnapshot("shop", "Shop", "Browse merchant stock"),
                "crafting" => new NpcServiceDefinitionSnapshot("shop", "Shop", "Browse merchant stock"),
                "quests" => new NpcServiceDefinitionSnapshot("quests", "Quests", "Review available work"),
                "training" => new NpcServiceDefinitionSnapshot("training", "Training", "Review skill progression"),
                "progression" => new NpcServiceDefinitionSnapshot("training", "Training", "Review skill progression"),
                _ => new NpcServiceDefinitionSnapshot(action, action, string.Empty)
            })
            .GroupBy(option => option.ActionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private static T[] ReadSection<T>(SqliteConnection connection, string section)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM gameplay_definitions WHERE section = $section LIMIT 1;";
        command.Parameters.AddWithValue("$section", section);
        var payload = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<T>();
        }

        return JsonSerializer.Deserialize<T[]>(payload, JsonOptions) ?? Array.Empty<T>();
    }

    private static void UpsertSection<T>(SqliteConnection connection, SqliteTransaction transaction, string section, IReadOnlyCollection<T> values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO gameplay_definitions(section, payload_json, updated_at_utc)
            VALUES ($section, $payload, $updated)
            ON CONFLICT(section) DO UPDATE SET
                payload_json = excluded.payload_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$section", section);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(values, JsonOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void Validate(GameplayDefinitionsSnapshot snapshot)
    {
        if (snapshot.Items.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate item ids are not allowed.");
        }

        if (snapshot.Skills.GroupBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate skill ids are not allowed.");
        }

        if (snapshot.Resources.Any(resource => snapshot.Items.All(item => !string.Equals(item.Id, resource.ItemId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Every resource definition must reference an existing item id.");
        }

        if (snapshot.Nodes.Any(node => snapshot.Resources.All(resource => !string.Equals(resource.Id, node.ResourceId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Every node definition must reference an existing resource id.");
        }

        if (snapshot.Npcs.GroupBy(npc => npc.NpcId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate npc ids are not allowed.");
        }

        if (snapshot.Npcs.Any(npc => snapshot.Zones.All(zone => zone.ZoneId != npc.ZoneId)))
        {
            throw new InvalidOperationException("Every npc definition must reference an existing zone id.");
        }

        if (snapshot.Npcs.Any(npc =>
                (npc.ServiceOptions ?? Array.Empty<NpcServiceDefinitionSnapshot>())
                .GroupBy(option => option.ActionId, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)))
        {
            throw new InvalidOperationException("NPC service options must not contain duplicate action ids.");
        }

        if (snapshot.PlayerSpawns.GroupBy(spawn => spawn.SpawnId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate player spawn ids are not allowed.");
        }

        if (snapshot.PlayerSpawns.Any(spawn => snapshot.Zones.All(zone => zone.ZoneId != spawn.ZoneId)))
        {
            throw new InvalidOperationException("Every player spawn definition must reference an existing zone id.");
        }

        if (snapshot.MobSpawns.GroupBy(spawn => spawn.SpawnId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Duplicate mob spawn ids are not allowed.");
        }

        if (snapshot.MobSpawns.Any(spawn => snapshot.Zones.All(zone => zone.ZoneId != spawn.ZoneId)))
        {
            throw new InvalidOperationException("Every mob spawn definition must reference an existing zone id.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
