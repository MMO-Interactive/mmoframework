using System;

namespace VoxelLibrary;

/// <summary>
/// Builds a Wurm-like tile terrain by snapping terrain samples to tile corners and
/// linearly interpolating across each tile.
/// </summary>
public sealed class TileHeightmapTerrainGenerator
{
    private readonly ProceduralVoxelWorldGenerator _source;

    public TileHeightmapTerrainGenerator(
        ProceduralVoxelWorldGenerator source,
        float tileSize = 4f,
        float maxCornerStep = 3f,
        float surfaceBlend = 2.5f,
        bool quantizeToHalfSteps = true)
    {
        if (tileSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive.");
        }

        if (maxCornerStep <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCornerStep), "Max corner step must be positive.");
        }

        if (surfaceBlend <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceBlend), "Surface blend must be positive.");
        }

        _source = source;
        TileSize = tileSize;
        MaxCornerStep = maxCornerStep;
        SurfaceBlend = surfaceBlend;
        QuantizeToHalfSteps = quantizeToHalfSteps;
    }

    public float TileSize { get; }

    public float MaxCornerStep { get; }

    public float SurfaceBlend { get; }

    public bool QuantizeToHalfSteps { get; }

    public float GetTerrainHeight(float x, float z)
    {
        var tileX = FastFloor(x / TileSize);
        var tileZ = FastFloor(z / TileSize);
        var originX = tileX * TileSize;
        var originZ = tileZ * TileSize;

        var h00 = GetCornerHeight(originX, originZ);
        var h10 = ClampStep(h00, GetCornerHeight(originX + TileSize, originZ));
        var h01 = ClampStep(h00, GetCornerHeight(originX, originZ + TileSize));
        var h11 = ClampStep((h10 + h01) * 0.5f, GetCornerHeight(originX + TileSize, originZ + TileSize));

        var tx = Clamp01((x - originX) / TileSize);
        var tz = Clamp01((z - originZ) / TileSize);

        var top = Lerp(h00, h10, tx);
        var bottom = Lerp(h01, h11, tx);
        return Lerp(top, bottom, tz);
    }

    public VoxelSample Sample(Float3 position)
    {
        var terrainHeight = GetTerrainHeight(position.X, position.Z);
        var density = Clamp((terrainHeight - position.Y) / SurfaceBlend, -1f, 1f);
        if (density < -0.05f)
        {
            return VoxelSample.Air;
        }

        var depth = terrainHeight - position.Y;
        var materialId = depth < 1.25f ? (ushort)2 : depth < 4.5f ? (ushort)1 : (ushort)3;
        return new VoxelSample(density, materialId);
    }

    public void PopulateChunk(VoxelChunk chunk)
    {
        for (var z = 0; z < chunk.SamplesPerAxis; z++)
        {
            for (var y = 0; y < chunk.SamplesPerAxis; y++)
            {
                for (var x = 0; x < chunk.SamplesPerAxis; x++)
                {
                    chunk.SetSample(x, y, z, Sample(chunk.GetSamplePosition(x, y, z)));
                }
            }
        }
    }

    private float GetCornerHeight(float x, float z)
    {
        var height = _source.GetTerrainHeight(x, z);
        return QuantizeToHalfSteps ? MathF.Round(height * 2f) * 0.5f : height;
    }

    private float ClampStep(float baseline, float candidate)
    {
        var min = baseline - MaxCornerStep;
        var max = baseline + MaxCornerStep;
        return Clamp(candidate, min, max);
    }

    private static float Lerp(float start, float end, float amount)
        => start + ((end - start) * amount);

    private static float Clamp01(float value)
        => Clamp(value, 0f, 1f);

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static int FastFloor(float value)
    {
        var truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }
}
