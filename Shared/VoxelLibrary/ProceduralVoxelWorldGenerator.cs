using System;

namespace VoxelLibrary;

public sealed class ProceduralVoxelWorldGenerator
{
    public const float DefaultWaterLevel = 8f;

    private const float MacroFrequencyMultiplier = 0.42f;
    private const float RidgeFrequencyMultiplier = 0.65f;
    private const float WarpFrequencyMultiplier = 0.38f;
    private const float ValleyFrequencyMultiplier = 0.26f;
    private const float RiverFrequencyMultiplier = 0.12f;

    public ProceduralVoxelWorldGenerator(
        int seed = 1337,
        float baseHeight = 10f,
        float terrainAmplitude = 12f,
        float terrainFrequency = 0.015f,
        float detailFrequency = 0.045f,
        float caveFrequency = 0.05f,
        float voxelSurfaceBlend = 2.5f)
    {
        Seed = seed;
        BaseHeight = baseHeight;
        TerrainAmplitude = terrainAmplitude;
        TerrainFrequency = terrainFrequency;
        DetailFrequency = detailFrequency;
        CaveFrequency = caveFrequency;
        VoxelSurfaceBlend = voxelSurfaceBlend;
    }

    public int Seed { get; }

    public float BaseHeight { get; }

    public float TerrainAmplitude { get; }

    public float TerrainFrequency { get; }

    public float DetailFrequency { get; }

    public float CaveFrequency { get; }

    public float VoxelSurfaceBlend { get; }

    public void PopulateChunk(VoxelChunk chunk)
    {
        for (var z = 0; z < chunk.SamplesPerAxis; z++)
        {
            for (var y = 0; y < chunk.SamplesPerAxis; y++)
            {
                for (var x = 0; x < chunk.SamplesPerAxis; x++)
                {
                    var position = chunk.GetSamplePosition(x, y, z);
                    chunk.SetSample(x, y, z, Sample(position));
                }
            }
        }
    }

    public VoxelSample Sample(Float3 position)
    {
        var terrainHeight = GetTerrainHeight(position.X, position.Z);
        var cliffMask = GetCliffMask(position.X, position.Z);
        var outcropMask = GetOutcropMask(position.X, position.Z);

        var surfaceDensity = Clamp((terrainHeight - position.Y) / VoxelSurfaceBlend, -1f, 1f);

        // Add restrained cliff lips so the world gets some rock structure without spawning giant floating slabs.
        if (cliffMask > 0.38f)
        {
            var shelfBand = 1f - MathF.Min(MathF.Abs((terrainHeight - 1.4f) - position.Y) / 3.2f, 1f);
            var shelfNoise = SampleNoise3D(position.X * 0.028f, position.Y * 0.041f, position.Z * 0.028f);
            var shelfDensity = ((shelfNoise * 0.42f) + (cliffMask * 0.5f) + (shelfBand * 0.38f)) - 0.72f;
            surfaceDensity = MathF.Max(surfaceDensity, shelfDensity);
        }

        if (outcropMask > 0.46f)
        {
            var outcropBand = 1f - MathF.Min(MathF.Abs((terrainHeight - 2.4f) - position.Y) / 2.8f, 1f);
            var outcropNoise = Remap01(SampleNoise3D((position.X - 55f) * 0.045f, position.Y * 0.05f, (position.Z + 88f) * 0.045f));
            var outcropDensity = ((outcropNoise * 0.52f) + (outcropMask * 0.42f) + (outcropBand * 0.28f)) - 0.7f;
            surfaceDensity = MathF.Max(surfaceDensity, outcropDensity);
        }

        if (surfaceDensity <= -1f)
        {
            return VoxelSample.Air;
        }

        var caveNoise = SampleNoise3D(position.X * CaveFrequency, position.Y * CaveFrequency, position.Z * CaveFrequency);
        var tunnelNoise = 1f - MathF.Abs(SampleNoise3D((position.X + 90f) * (CaveFrequency * 0.72f), position.Y * (CaveFrequency * 0.95f), (position.Z - 140f) * (CaveFrequency * 0.72f)));
        var chamberNoise = Remap01(SampleNoise3D((position.X - 260f) * (CaveFrequency * 0.46f), (position.Y + 80f) * (CaveFrequency * 0.46f), (position.Z + 310f) * (CaveFrequency * 0.46f)));
        var caveThreshold = 0.57f + (SampleNoise2D(position.X * 0.01f, position.Z * 0.01f) * 0.08f);
        var tunnelThreshold = 0.84f - (cliffMask * 0.08f);
        var chamberThreshold = 0.82f - (cliffMask * 0.05f);
        var withinCaveBand = position.Y > 2f && position.Y < terrainHeight - 6f;

        if (withinCaveBand && caveNoise > caveThreshold)
        {
            var caveBlend = Clamp((caveNoise - caveThreshold) / 0.2f, 0f, 1f);
            surfaceDensity = MathF.Min(surfaceDensity, Lerp(0.15f, -1f, caveBlend));
        }

        if (withinCaveBand && tunnelNoise > tunnelThreshold && position.Y < terrainHeight - 7f)
        {
            var tunnelBlend = Clamp((tunnelNoise - tunnelThreshold) / 0.14f, 0f, 1f);
            surfaceDensity = MathF.Min(surfaceDensity, Lerp(0.28f, -1f, tunnelBlend));
        }

        if (withinCaveBand && chamberNoise > chamberThreshold && position.Y < terrainHeight - 10f)
        {
            var chamberBlend = Clamp((chamberNoise - chamberThreshold) / 0.12f, 0f, 1f);
            surfaceDensity = MathF.Min(surfaceDensity, Lerp(0.45f, -1f, chamberBlend));
        }

        if (surfaceDensity < -0.05f)
        {
            return VoxelSample.Air;
        }

        var materialId = SelectMaterial(position.Y, terrainHeight, caveNoise);
        return new VoxelSample(surfaceDensity, materialId);
    }

    public float GetTerrainHeight(float x, float z)
    {
        var warped = GetWarpedPosition(x, z);
        var warpedX = warped.X;
        var warpedZ = warped.Z;

        var macroShape = SampleNoise2D(warpedX * (TerrainFrequency * MacroFrequencyMultiplier), warpedZ * (TerrainFrequency * MacroFrequencyMultiplier));
        var continentMask = Remap01(SampleNoise2D((warpedX - 900f) * (TerrainFrequency * 0.18f), (warpedZ + 500f) * (TerrainFrequency * 0.18f)));
        var hillShape = SampleNoise2D(warpedX * TerrainFrequency, warpedZ * TerrainFrequency);
        var detailShape = SampleNoise2D((warpedX + 117f) * DetailFrequency, (warpedZ - 63f) * DetailFrequency);
        var erosionMajor = Remap01(SampleNoise2D((warpedX - 120f) * (TerrainFrequency * 0.2f), (warpedZ + 80f) * (TerrainFrequency * 0.2f)));
        var erosionMinor = Remap01(SampleNoise2D((warpedX + 330f) * (TerrainFrequency * 0.44f), (warpedZ - 270f) * (TerrainFrequency * 0.44f)));
        var erosionMask = SmoothStep(0.36f, 0.86f, (erosionMajor * 0.68f) + (erosionMinor * 0.32f));
        var outcropNoise = Remap01(SampleNoise2D((warpedX + 420f) * (TerrainFrequency * 0.5f), (warpedZ - 230f) * (TerrainFrequency * 0.5f)));

        var primaryRidge = 1f - MathF.Abs(SampleNoise2D((warpedX - 220f) * (TerrainFrequency * RidgeFrequencyMultiplier), (warpedZ + 140f) * (TerrainFrequency * RidgeFrequencyMultiplier)));
        var secondaryRidge = 1f - MathF.Abs(SampleNoise2D((warpedX + 540f) * (TerrainFrequency * 0.9f), (warpedZ - 320f) * (TerrainFrequency * 0.9f)));
        var ridgeMask = MathF.Pow(Clamp(primaryRidge * 0.72f + secondaryRidge * 0.28f, 0f, 1f), 2.35f);

        var valleyBase = 1f - MathF.Abs(SampleNoise2D((warpedX + 160f) * (TerrainFrequency * ValleyFrequencyMultiplier), (warpedZ - 280f) * (TerrainFrequency * ValleyFrequencyMultiplier)));
        var valleyDetail = 1f - MathF.Abs(SampleNoise2D((warpedX - 70f) * (TerrainFrequency * 0.48f), (warpedZ + 95f) * (TerrainFrequency * 0.48f)));
        var valleyMask = MathF.Pow(Clamp((valleyBase * 0.7f) + (valleyDetail * 0.3f), 0f, 1f), 5.2f);

        var basinMask = MathF.Max(0f, 0.52f - Remap01(SampleNoise2D((warpedX + 800f) * (TerrainFrequency * 0.22f), (warpedZ - 650f) * (TerrainFrequency * 0.22f)))) / 0.52f;
        basinMask = MathF.Pow(Clamp(basinMask, 0f, 1f), 1.8f);
        var riverMask = GetRiverMaskInternal(warpedX, warpedZ);
        var highlandMask = SmoothStep(0.5f, 0.9f, (continentMask * 0.58f) + (ridgeMask * 0.42f));

        var plateauMask = SmoothStep(0.56f, 0.84f, ridgeMask + (continentMask * 0.18f));

        var height =
            BaseHeight +
            (macroShape * TerrainAmplitude * 0.95f) +
            (hillShape * TerrainAmplitude * 0.45f) +
            (detailShape * TerrainAmplitude * 0.16f) +
            (ridgeMask * TerrainAmplitude * 1.65f) -
            (valleyMask * TerrainAmplitude * 1.95f) -
            (basinMask * TerrainAmplitude * 1.15f) +
            ((continentMask - 0.5f) * TerrainAmplitude * 1.55f) +
            (highlandMask * TerrainAmplitude * 0.85f) +
            (erosionMask * ridgeMask * TerrainAmplitude * 0.32f);

        var plateauHeight = BaseHeight + (TerrainAmplitude * (1.1f + (continentMask * 0.7f)));
        height = Lerp(height, plateauHeight, plateauMask * 0.35f);

        var shelfMask = SmoothStep(0.52f, 0.88f, (highlandMask * 0.68f) + (outcropNoise * 0.32f));
        var shelfStep = 3f;
        var shelfHeight = MathF.Round(height / shelfStep) * shelfStep;
        height = Lerp(height, shelfHeight, shelfMask * 0.18f);

        var terraceStrength = SmoothStep(0.58f, 0.9f, ridgeMask) * 0.45f;
        if (terraceStrength > 0f)
        {
            var terraceStep = 2.4f;
            var terracedHeight = MathF.Round(height / terraceStep) * terraceStep;
            height = Lerp(height, terracedHeight, terraceStrength);
        }

        var riverFloor = DefaultWaterLevel - 1.4f + (continentMask * 0.6f);
        var carvedRiverHeight = riverFloor + (macroShape * 0.65f) + (detailShape * 0.35f);
        var riverBlend = SmoothStep(0.12f, 0.92f, valleyMask) * riverMask;
        height = Lerp(height, MathF.Min(height, carvedRiverHeight), riverBlend * 0.92f);

        var wetLowlandMask = SmoothStep(0.48f, 0.84f, GetMoisture(x, z)) * SmoothStep(0.22f, 0.82f, basinMask + (riverMask * 0.7f));
        var marshFloor = DefaultWaterLevel - 0.7f + (detailShape * 0.28f);
        height = Lerp(height, MathF.Min(height, marshFloor), wetLowlandMask * 0.22f);

        return height;
    }

    public float GetRiverMask(float x, float z)
    {
        var warped = GetWarpedPosition(x, z);
        return GetRiverMaskInternal(warped.X, warped.Z);
    }

    public float GetWaterSurfaceHeight(float x, float z)
    {
        var riverMask = GetRiverMask(x, z);
        var basinMask = MathF.Max(0f, 0.52f - Remap01(SampleNoise2D((x + 800f) * (TerrainFrequency * 0.22f), (z - 650f) * (TerrainFrequency * 0.22f)))) / 0.52f;
        var moisture = GetMoisture(x, z);
        if (riverMask < 0.42f && basinMask < 0.4f && moisture < 0.5f)
        {
            return float.NegativeInfinity;
        }

        return DefaultWaterLevel + ((moisture - 0.5f) * 0.35f);
    }

    public float GetErosionMask(float x, float z)
    {
        var warped = GetWarpedPosition(x, z);
        var major = Remap01(SampleNoise2D((warped.X - 120f) * (TerrainFrequency * 0.2f), (warped.Z + 80f) * (TerrainFrequency * 0.2f)));
        var minor = Remap01(SampleNoise2D((warped.X + 330f) * (TerrainFrequency * 0.44f), (warped.Z - 270f) * (TerrainFrequency * 0.44f)));
        return SmoothStep(0.36f, 0.86f, (major * 0.68f) + (minor * 0.32f));
    }

    public float GetMoisture(float x, float z)
        => Remap01(SampleNoise2D((x - 145f) * 0.013f, (z + 210f) * 0.013f));

    public float GetTemperature(float x, float z)
        => Remap01(SampleNoise2D((x + 260f) * 0.011f, (z - 340f) * 0.011f));

    public float GetHighlandMask(float x, float z)
    {
        var warped = GetWarpedPosition(x, z);
        var continentMask = Remap01(SampleNoise2D((warped.X - 900f) * (TerrainFrequency * 0.18f), (warped.Z + 500f) * (TerrainFrequency * 0.18f)));
        var primaryRidge = 1f - MathF.Abs(SampleNoise2D((warped.X - 220f) * (TerrainFrequency * RidgeFrequencyMultiplier), (warped.Z + 140f) * (TerrainFrequency * RidgeFrequencyMultiplier)));
        var secondaryRidge = 1f - MathF.Abs(SampleNoise2D((warped.X + 540f) * (TerrainFrequency * 0.9f), (warped.Z - 320f) * (TerrainFrequency * 0.9f)));
        var ridgeMask = MathF.Pow(Clamp(primaryRidge * 0.72f + secondaryRidge * 0.28f, 0f, 1f), 2.35f);
        return SmoothStep(0.5f, 0.9f, (continentMask * 0.58f) + (ridgeMask * 0.42f));
    }

    public float GetCliffMask(float x, float z)
    {
        var warped = GetWarpedPosition(x, z);
        var warpedX = warped.X;
        var warpedZ = warped.Z;
        var majorRidge = 1f - MathF.Abs(SampleNoise2D((warpedX - 220f) * (TerrainFrequency * RidgeFrequencyMultiplier), (warpedZ + 140f) * (TerrainFrequency * RidgeFrequencyMultiplier)));
        var cliffNoise = Remap01(SampleNoise2D((warpedX + 170f) * (TerrainFrequency * 0.32f), (warpedZ - 90f) * (TerrainFrequency * 0.32f)));
        return SmoothStep(0.52f, 0.88f, majorRidge) * SmoothStep(0.35f, 0.82f, cliffNoise);
    }

    public float GetOutcropMask(float x, float z)
    {
        var warped = GetWarpedPosition(x, z);
        var rockyNoise = Remap01(SampleNoise2D((warped.X + 420f) * (TerrainFrequency * 0.5f), (warped.Z - 230f) * (TerrainFrequency * 0.5f)));
        var erosionMask = GetErosionMask(x, z);
        var cliffMask = GetCliffMask(x, z);
        return SmoothStep(0.42f, 0.92f, (rockyNoise * 0.4f) + (erosionMask * 0.2f) + (cliffMask * 0.4f));
    }

    public float GetForestDensity(float x, float z)
    {
        var moisture = GetMoisture(x, z);
        var temperature = GetTemperature(x, z);
        var riverMask = GetRiverMask(x, z);
        var highlandMask = GetHighlandMask(x, z);
        var forestNoise = Remap01(SampleNoise2D((x - 180f) * 0.028f, (z + 150f) * 0.028f));
        var groveNoise = Remap01(SampleNoise2D((x + 420f) * 0.061f, (z - 360f) * 0.061f));
        var climateSuitability = Clamp((moisture * 1.2f) - MathF.Max(0f, temperature - 0.74f) - (highlandMask * 0.22f), 0f, 1f);
        return Clamp((forestNoise * 0.55f) + (groveNoise * 0.2f) + (riverMask * 0.15f) + (climateSuitability * 0.35f), 0f, 1f);
    }

    public float GetMarshMask(float x, float z)
    {
        var moisture = GetMoisture(x, z);
        var riverMask = GetRiverMask(x, z);
        var terrainHeight = GetTerrainHeight(x, z);
        var waterHeight = GetWaterSurfaceHeight(x, z);
        var lowlandMask = SmoothStep(DefaultWaterLevel - 3.5f, DefaultWaterLevel + 4f, terrainHeight);
        lowlandMask = 1f - lowlandMask;
        var shallowFlooding = float.IsNegativeInfinity(waterHeight)
            ? 0f
            : SmoothStep(0.15f, 1.4f, waterHeight - terrainHeight);
        return Clamp((moisture * 0.45f) + (riverMask * 0.3f) + (lowlandMask * 0.25f) + (shallowFlooding * 0.4f) - 0.28f, 0f, 1f);
    }

    private ushort SelectMaterial(float y, float terrainHeight, float caveNoise)
    {
        if (terrainHeight - y < 1.4f)
        {
            return 2;
        }

        if (terrainHeight - y < 4.5f)
        {
            return 1;
        }

        return caveNoise > 0.45f ? (ushort)4 : (ushort)3;
    }

    private (float X, float Z) GetWarpedPosition(float x, float z)
    {
        var warpX = SampleNoise2D((x + 310f) * (TerrainFrequency * WarpFrequencyMultiplier), (z - 180f) * (TerrainFrequency * WarpFrequencyMultiplier)) * (TerrainAmplitude * 1.6f);
        var warpZ = SampleNoise2D((x - 260f) * (TerrainFrequency * WarpFrequencyMultiplier), (z + 420f) * (TerrainFrequency * WarpFrequencyMultiplier)) * (TerrainAmplitude * 1.6f);
        return (x + warpX, z + warpZ);
    }

    private float SampleNoise2D(float x, float z)
    {
        var x0 = FastFloor(x);
        var z0 = FastFloor(z);
        var tx = x - x0;
        var tz = z - z0;

        var v00 = HashToSignedFloat(x0, 0, z0);
        var v10 = HashToSignedFloat(x0 + 1, 0, z0);
        var v01 = HashToSignedFloat(x0, 0, z0 + 1);
        var v11 = HashToSignedFloat(x0 + 1, 0, z0 + 1);

        var sx = Smooth(tx);
        var sz = Smooth(tz);
        var ix0 = Lerp(v00, v10, sx);
        var ix1 = Lerp(v01, v11, sx);
        return Lerp(ix0, ix1, sz);
    }

    private float SampleNoise3D(float x, float y, float z)
    {
        var x0 = FastFloor(x);
        var y0 = FastFloor(y);
        var z0 = FastFloor(z);
        var tx = x - x0;
        var ty = y - y0;
        var tz = z - z0;

        var sx = Smooth(tx);
        var sy = Smooth(ty);
        var sz = Smooth(tz);

        var c000 = HashToSignedFloat(x0, y0, z0);
        var c100 = HashToSignedFloat(x0 + 1, y0, z0);
        var c010 = HashToSignedFloat(x0, y0 + 1, z0);
        var c110 = HashToSignedFloat(x0 + 1, y0 + 1, z0);
        var c001 = HashToSignedFloat(x0, y0, z0 + 1);
        var c101 = HashToSignedFloat(x0 + 1, y0, z0 + 1);
        var c011 = HashToSignedFloat(x0, y0 + 1, z0 + 1);
        var c111 = HashToSignedFloat(x0 + 1, y0 + 1, z0 + 1);

        var x00 = Lerp(c000, c100, sx);
        var x10 = Lerp(c010, c110, sx);
        var x01 = Lerp(c001, c101, sx);
        var x11 = Lerp(c011, c111, sx);
        var y0Blend = Lerp(x00, x10, sy);
        var y1Blend = Lerp(x01, x11, sy);
        return Lerp(y0Blend, y1Blend, sz);
    }

    private float HashToSignedFloat(int x, int y, int z)
    {
        unchecked
        {
            var hash = Seed;
            hash = (hash * 397) ^ x;
            hash = (hash * 397) ^ y;
            hash = (hash * 397) ^ z;
            hash ^= hash >> 16;
            hash *= 0x45d9f3b;
            hash ^= hash >> 16;
            var normalized = (hash & 0x7fffffff) / (float)int.MaxValue;
            return (normalized * 2f) - 1f;
        }
    }

    private static float Smooth(float value)
        => value * value * (3f - (2f * value));

    private static float Lerp(float start, float end, float amount)
        => start + ((end - start) * amount);

    private float GetRiverMaskInternal(float warpedX, float warpedZ)
    {
        var trunk = 1f - MathF.Abs(SampleNoise2D((warpedX + 420f) * (TerrainFrequency * RiverFrequencyMultiplier), (warpedZ - 110f) * (TerrainFrequency * RiverFrequencyMultiplier)));
        var tributary = 1f - MathF.Abs(SampleNoise2D((warpedX - 610f) * (TerrainFrequency * 0.2f), (warpedZ + 330f) * (TerrainFrequency * 0.2f)));
        var broadMask = Remap01(SampleNoise2D((warpedX + 95f) * (TerrainFrequency * 0.06f), (warpedZ - 245f) * (TerrainFrequency * 0.06f)));
        var riverMask = MathF.Pow(Clamp((trunk * 0.74f) + (tributary * 0.26f), 0f, 1f), 6.5f);
        return riverMask * SmoothStep(0.28f, 0.9f, broadMask);
    }

    private static float Remap01(float value)
        => (value * 0.5f) + 0.5f;

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (edge0 >= edge1)
        {
            return value >= edge1 ? 1f : 0f;
        }

        var t = Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

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
