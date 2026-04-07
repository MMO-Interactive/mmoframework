using System.Text.Json;
using Microsoft.Xna.Framework;

namespace MonoGameEngine.Editor.Mmorpg;

public static class MmorpgWorkspacePersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Save(MmorpgEditorWorkspace workspace, string filePath)
    {
        var payload = new WorkspaceSaveModel
        {
            Seed = workspace.Seed,
            ZoneId = workspace.ActiveZone.ZoneId,
            ZoneWidthTiles = workspace.ActiveZone.WidthTiles,
            ZoneHeightTiles = workspace.ActiveZone.HeightTiles,
            NpcSpawns = workspace.NpcSpawns
                .Select(n => new NpcSpawnSaveModel(n.NpcArchetype, n.Level, n.Position.X, n.Position.Y))
                .ToList(),
            ResourceSpawns = workspace.ResourceSpawns
                .Select(r => new ResourceSpawnSaveModel(r.ResourceType, r.Position.X, r.Position.Y))
                .ToList(),
            StructureBoxes = workspace.StructureBoxes
                .Select(s => new StructureBoxSaveModel(s.Min.X, s.Min.Y, s.Max.X, s.Max.Y, s.BaseY, s.TopY, s.MaterialId))
                .ToList()
        };

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static void LoadInto(MmorpgEditorWorkspace workspace, string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var json = File.ReadAllText(filePath);
        var payload = JsonSerializer.Deserialize<WorkspaceSaveModel>(json, JsonOptions);
        if (payload is null)
        {
            return;
        }

        workspace.RestoreState(
            payload.Seed,
            new ZoneDefinition(payload.ZoneId, payload.ZoneWidthTiles, payload.ZoneHeightTiles),
            payload.NpcSpawns.Select(npc => new NpcSpawnPoint(npc.NpcArchetype, npc.Level, new Vector2(npc.X, npc.Z))),
            payload.ResourceSpawns.Select(resource => new ResourceSpawnPoint(resource.ResourceType, new Vector2(resource.X, resource.Z))),
            payload.StructureBoxes.Select(s => new StructureBoxPlacement(new Point(s.MinX, s.MinZ), new Point(s.MaxX, s.MaxZ), s.BaseY, s.TopY, s.MaterialId)));
    }

    public sealed class WorkspaceSaveModel
    {
        public string ZoneId { get; set; } = "starter_zone";
        public int Seed { get; set; } = 1337;
        public int ZoneWidthTiles { get; set; } = 256;
        public int ZoneHeightTiles { get; set; } = 256;
        public List<NpcSpawnSaveModel> NpcSpawns { get; set; } = new();
        public List<ResourceSpawnSaveModel> ResourceSpawns { get; set; } = new();
        public List<StructureBoxSaveModel> StructureBoxes { get; set; } = new();
    }

    public sealed record NpcSpawnSaveModel(string NpcArchetype, int Level, float X, float Z);

    public sealed record ResourceSpawnSaveModel(string ResourceType, float X, float Z);

    public sealed record StructureBoxSaveModel(int MinX, int MinZ, int MaxX, int MaxZ, int BaseY, int TopY, ushort MaterialId);
}
