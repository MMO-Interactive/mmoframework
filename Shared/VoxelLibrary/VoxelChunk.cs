using System;

namespace VoxelLibrary;

public sealed class VoxelChunk
{
    private readonly VoxelSample[] _samples;

    public VoxelChunk(Int3 chunkCoordinate, int resolution, float voxelSize)
    {
        if (resolution <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), "Chunk resolution must be positive.");
        }

        if (voxelSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be positive.");
        }

        ChunkCoordinate = chunkCoordinate;
        Resolution = resolution;
        VoxelSize = voxelSize;
        SamplesPerAxis = resolution + 1;
        _samples = new VoxelSample[SamplesPerAxis * SamplesPerAxis * SamplesPerAxis];
        Fill(VoxelSample.Air);
    }

    public Int3 ChunkCoordinate { get; }

    public int Resolution { get; }

    public int SamplesPerAxis { get; }

    public float VoxelSize { get; }

    public Float3 Origin => new(
        ChunkCoordinate.X * Resolution * VoxelSize,
        ChunkCoordinate.Y * Resolution * VoxelSize,
        ChunkCoordinate.Z * Resolution * VoxelSize);

    public void Fill(VoxelSample sample)
    {
        for (var i = 0; i < _samples.Length; i++)
        {
            _samples[i] = sample;
        }
    }

    public VoxelSample GetSample(int x, int y, int z)
        => _samples[ToIndex(x, y, z)];

    public void SetSample(int x, int y, int z, VoxelSample sample)
        => _samples[ToIndex(x, y, z)] = sample;

    public Float3 GetSamplePosition(int x, int y, int z)
    {
        var origin = Origin;
        return new Float3(
            origin.X + (x * VoxelSize),
            origin.Y + (y * VoxelSize),
            origin.Z + (z * VoxelSize));
    }

    private int ToIndex(int x, int y, int z)
    {
        if ((uint)x >= SamplesPerAxis || (uint)y >= SamplesPerAxis || (uint)z >= SamplesPerAxis)
        {
            throw new ArgumentOutOfRangeException($"Sample coordinate ({x}, {y}, {z}) is outside chunk bounds 0..{SamplesPerAxis - 1}.");
        }

        return x + (y * SamplesPerAxis) + (z * SamplesPerAxis * SamplesPerAxis);
    }
}
