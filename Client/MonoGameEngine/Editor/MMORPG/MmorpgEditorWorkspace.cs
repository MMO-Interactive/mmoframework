using Microsoft.Xna.Framework;
using VoxelLibrary;

namespace MonoGameEngine.Editor.Mmorpg;

public sealed class MmorpgEditorWorkspace
{
    private int _seed = 1337;

    public MmorpgEditorWorkspace()
    {
        ActiveZone = new ZoneDefinition("starter_zone", 256, 256);
        TerrainGenerator = CreateTerrainGenerator();
    }

    public ZoneDefinition ActiveZone { get; private set; }

    public int Seed => _seed;

    public AdaptiveTerrainGenerator TerrainGenerator { get; private set; }

    public List<NpcSpawnPoint> NpcSpawns { get; } = new();

    public List<ResourceSpawnPoint> ResourceSpawns { get; } = new();

    public List<StructureBoxPlacement> StructureBoxes { get; } = new();

    public void SetZone(string zoneId, int widthTiles = 256, int heightTiles = 256)
    {
        ActiveZone = new ZoneDefinition(zoneId, widthTiles, heightTiles);
    }

    public void AddNpcSpawn(string npcArchetype, int level, Vector2 position)
        => NpcSpawns.Add(new NpcSpawnPoint(npcArchetype, level, position));

    public void AddResourceSpawn(string resourceType, Vector2 position)
        => ResourceSpawns.Add(new ResourceSpawnPoint(resourceType, position));

    public void PlaceStructureBox(Point minGrid, Point maxGrid, int baseY, int topY, ushort materialId)
    {
        var placement = new StructureBoxPlacement(minGrid, maxGrid, baseY, topY, materialId);
        StructureBoxes.Add(placement);
        ApplyStructureBox(placement);
    }

    public void RegenerateTerrain()
    {
        _seed += 17;
        TerrainGenerator = CreateTerrainGenerator();
        ReapplyStructures();
    }

    public void RestoreState(
        int seed,
        ZoneDefinition activeZone,
        IEnumerable<NpcSpawnPoint> npcSpawns,
        IEnumerable<ResourceSpawnPoint> resourceSpawns,
        IEnumerable<StructureBoxPlacement> structureBoxes)
    {
        _seed = seed;
        ActiveZone = activeZone;

        NpcSpawns.Clear();
        ResourceSpawns.Clear();
        StructureBoxes.Clear();

        NpcSpawns.AddRange(npcSpawns);
        ResourceSpawns.AddRange(resourceSpawns);
        StructureBoxes.AddRange(structureBoxes);

        TerrainGenerator = CreateTerrainGenerator();
        ReapplyStructures();
    }

    private AdaptiveTerrainGenerator CreateTerrainGenerator()
    {
        return new AdaptiveTerrainGenerator(
            engineKind: TerrainEngineKind.Hybrid,
            seed: _seed,
            tileSize: 4f,
            maxTileCornerStep: 2.5f,
            structureChunkResolution: 16,
            structureVoxelSize: 1f);
    }

    private void ReapplyStructures()
    {
        foreach (var structure in StructureBoxes)
        {
            ApplyStructureBox(structure);
        }
    }

    private void ApplyStructureBox(StructureBoxPlacement placement)
    {
        TerrainGenerator.AddStructureBox(
            new Int3(placement.Min.X, placement.BaseY, placement.Min.Y),
            new Int3(placement.Max.X, placement.TopY, placement.Max.Y),
            placement.MaterialId);
    }
}
