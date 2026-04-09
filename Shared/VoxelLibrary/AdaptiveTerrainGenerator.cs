using System;

namespace VoxelLibrary;

/// <summary>
/// Terrain generator that can run voxel, tiled-heightmap, or a hybrid model where
/// terrain is tile-based and player-made structures are voxel-based.
/// </summary>
public sealed class AdaptiveTerrainGenerator
{
    private readonly ProceduralVoxelWorldGenerator _voxelGenerator;
    private readonly TileHeightmapTerrainGenerator _tileGenerator;
    private readonly VoxelWorld _structureWorld;

    public AdaptiveTerrainGenerator(
        TerrainEngineKind engineKind = TerrainEngineKind.Hybrid,
        int seed = 1337,
        float tileSize = 4f,
        float maxTileCornerStep = 3f,
        int structureChunkResolution = 16,
        float structureVoxelSize = 1f)
    {
        if (structureChunkResolution <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(structureChunkResolution), "Structure chunk resolution must be positive.");
        }

        if (structureVoxelSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(structureVoxelSize), "Structure voxel size must be positive.");
        }

        EngineKind = engineKind;
        _voxelGenerator = new ProceduralVoxelWorldGenerator(seed: seed);
        _tileGenerator = new TileHeightmapTerrainGenerator(
            _voxelGenerator,
            tileSize: tileSize,
            maxCornerStep: maxTileCornerStep,
            surfaceBlend: _voxelGenerator.VoxelSurfaceBlend);
        _structureWorld = new VoxelWorld(structureChunkResolution, structureVoxelSize);
    }

    public TerrainEngineKind EngineKind { get; }

    public void PopulateChunk(VoxelChunk chunk)
    {
        switch (EngineKind)
        {
            case TerrainEngineKind.Voxel:
                _voxelGenerator.PopulateChunk(chunk);
                return;
            case TerrainEngineKind.Tile:
                _tileGenerator.PopulateChunk(chunk);
                return;
            default:
                PopulateHybridChunk(chunk);
                return;
        }
    }

    public VoxelSample Sample(Float3 position)
    {
        return EngineKind switch
        {
            TerrainEngineKind.Voxel => _voxelGenerator.Sample(position),
            TerrainEngineKind.Tile => _tileGenerator.Sample(position),
            _ => SampleHybrid(position),
        };
    }

    /// <summary>
    /// Sets a voxel used by the hybrid structure layer. Intended for player construction.
    /// </summary>
    public void SetStructureVoxel(int x, int y, int z, VoxelSample sample)
        => _structureWorld.SetSampleGlobal(x, y, z, sample);

    public VoxelSample GetStructureVoxel(int x, int y, int z)
        => _structureWorld.GetSampleGlobal(x, y, z);


    public void AddStructureBox(Int3 minInclusive, Int3 maxInclusive, ushort materialId)
    {
        for (var z = minInclusive.Z; z <= maxInclusive.Z; z++)
        {
            for (var y = minInclusive.Y; y <= maxInclusive.Y; y++)
            {
                for (var x = minInclusive.X; x <= maxInclusive.X; x++)
                {
                    _structureWorld.SetSampleGlobal(x, y, z, new VoxelSample(1f, materialId));
                }
            }
        }
    }

    public float GetTerrainHeight(float x, float z)
    {
        return EngineKind switch
        {
            TerrainEngineKind.Voxel => _voxelGenerator.GetTerrainHeight(x, z),
            _ => _tileGenerator.GetTerrainHeight(x, z),
        };
    }

    public float GetRiverMask(float x, float z) => _voxelGenerator.GetRiverMask(x, z);

    public float GetWaterSurfaceHeight(float x, float z)
    {
        var surface = _voxelGenerator.GetWaterSurfaceHeight(x, z);
        if (float.IsNegativeInfinity(surface))
        {
            var tileHeight = _tileGenerator.GetTerrainHeight(x, z);
            if (tileHeight < ProceduralVoxelWorldGenerator.DefaultWaterLevel - 1f)
            {
                return ProceduralVoxelWorldGenerator.DefaultWaterLevel;
            }
        }

        return surface;
    }

    public float GetErosionMask(float x, float z) => _voxelGenerator.GetErosionMask(x, z);

    public float GetMoisture(float x, float z) => _voxelGenerator.GetMoisture(x, z);

    public float GetTemperature(float x, float z) => _voxelGenerator.GetTemperature(x, z);

    public float GetHighlandMask(float x, float z) => _voxelGenerator.GetHighlandMask(x, z);

    public float GetCliffMask(float x, float z) => _voxelGenerator.GetCliffMask(x, z);

    public float GetOutcropMask(float x, float z) => _voxelGenerator.GetOutcropMask(x, z);

    public float GetForestDensity(float x, float z) => _voxelGenerator.GetForestDensity(x, z);

    public float GetMarshMask(float x, float z) => _voxelGenerator.GetMarshMask(x, z);

    private void PopulateHybridChunk(VoxelChunk chunk)
    {
        for (var z = 0; z < chunk.SamplesPerAxis; z++)
        {
            for (var y = 0; y < chunk.SamplesPerAxis; y++)
            {
                for (var x = 0; x < chunk.SamplesPerAxis; x++)
                {
                    var position = chunk.GetSamplePosition(x, y, z);
                    chunk.SetSample(x, y, z, SampleHybrid(position));
                }
            }
        }
    }

    private VoxelSample SampleHybrid(Float3 position)
    {
        // Hybrid = Wurm-like tile terrain + voxel structures.
        var terrainSample = _tileGenerator.Sample(position);

        var structureGrid = _structureWorld.WorldToGridPosition(position);
        var structureSample = _structureWorld.GetSampleGlobal(structureGrid.X, structureGrid.Y, structureGrid.Z);
        if (structureSample.Density <= -0.05f)
        {
            return terrainSample;
        }

        var structureDensity = Clamp(structureSample.Density, -1f, 1f);
        if (structureDensity > terrainSample.Density)
        {
            var materialId = structureSample.MaterialId != 0 ? structureSample.MaterialId : (ushort)9;
            return new VoxelSample(structureDensity, materialId);
        }

        return terrainSample;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
