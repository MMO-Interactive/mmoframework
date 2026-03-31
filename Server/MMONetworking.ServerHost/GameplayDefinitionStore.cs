using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class GameplayDefinitionStore
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private GameplayDefinitionsSnapshot _snapshot;

    public GameplayDefinitionStore(string filePath, ZoneDirectory zoneDirectory)
    {
        _filePath = filePath;
        _snapshot = LoadOrCreate(filePath, zoneDirectory);
    }

    public GameplayDefinitionsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public GameplayDefinitionsSnapshot Update(GameplayDefinitionsSnapshot next)
    {
        Validate(next);
        lock (_sync)
        {
            _snapshot = next;
            Persist(_snapshot);
            return _snapshot;
        }
    }

    private static GameplayDefinitionsSnapshot LoadOrCreate(string filePath, ZoneDirectory zoneDirectory)
    {
        if (File.Exists(filePath))
        {
            var existing = JsonSerializer.Deserialize<GameplayDefinitionsSnapshot>(File.ReadAllText(filePath), JsonOptions);
            if (existing is not null)
            {
                return existing;
            }
        }

        var bootstrap = new GameplayDefinitionsSnapshot(
            Items: new[]
            {
                new ItemDefinitionSnapshot("log", "Wood Log", 200, 1.0f),
                new ItemDefinitionSnapshot("ore", "Iron Ore", 200, 1.5f)
            },
            Skills: new[]
            {
                new SkillDefinitionSnapshot("gathering", "Gathering", 100),
                new SkillDefinitionSnapshot("mining", "Mining", 100)
            },
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
                .Select(zone => new ZoneDefinitionSnapshot(zone.ZoneId, zone.Name, zone.MinX, zone.MaxX, zone.MinZ, zone.MaxZ))
                .ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        File.WriteAllText(filePath, JsonSerializer.Serialize(bootstrap, JsonOptions));
        return bootstrap;
    }

    private void Persist(GameplayDefinitionsSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
        File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOptions));
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
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
