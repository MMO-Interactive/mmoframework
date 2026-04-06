using System;
using System.Collections.Generic;

namespace VoxelLibrary;

public sealed class VoxelWorld
{
    private readonly Dictionary<Int3, VoxelChunk> _chunks = new();

    public VoxelWorld(int chunkResolution, float voxelSize)
    {
        if (chunkResolution <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkResolution), "Chunk resolution must be positive.");
        }

        if (voxelSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be positive.");
        }

        ChunkResolution = chunkResolution;
        VoxelSize = voxelSize;
    }

    public int ChunkResolution { get; }

    public float VoxelSize { get; }

    public IEnumerable<VoxelChunk> Chunks => _chunks.Values;

    public VoxelChunk GetOrCreateChunk(Int3 chunkCoordinate)
    {
        if (_chunks.TryGetValue(chunkCoordinate, out var existing))
        {
            return existing;
        }

        var created = new VoxelChunk(chunkCoordinate, ChunkResolution, VoxelSize);
        _chunks.Add(chunkCoordinate, created);
        return created;
    }

    public bool TryGetChunk(Int3 chunkCoordinate, out VoxelChunk? chunk)
        => _chunks.TryGetValue(chunkCoordinate, out chunk);

    public bool RemoveChunk(Int3 chunkCoordinate)
        => _chunks.Remove(chunkCoordinate);

    public void AddOrReplaceChunk(VoxelChunk chunk)
        => _chunks[chunk.ChunkCoordinate] = chunk;

    public VoxelSample GetSampleGlobal(int x, int y, int z)
    {
        var (chunkCoordinate, localCoordinate) = ToChunkAddress(new Int3(x, y, z));
        return _chunks.TryGetValue(chunkCoordinate, out var chunk)
            ? chunk.GetSample(localCoordinate.X, localCoordinate.Y, localCoordinate.Z)
            : VoxelSample.Air;
    }

    public void SetSampleGlobal(int x, int y, int z, VoxelSample sample)
    {
        var (chunkCoordinate, localCoordinate) = ToChunkAddress(new Int3(x, y, z));
        var chunk = GetOrCreateChunk(chunkCoordinate);
        chunk.SetSample(localCoordinate.X, localCoordinate.Y, localCoordinate.Z, sample);
    }

    public Float3 GridToWorldPosition(int x, int y, int z)
        => new(x * VoxelSize, y * VoxelSize, z * VoxelSize);

    public Int3 WorldToGridPosition(Float3 position)
        => new(
            FastFloor(position.X / VoxelSize),
            FastFloor(position.Y / VoxelSize),
            FastFloor(position.Z / VoxelSize));

    private (Int3 ChunkCoordinate, Int3 LocalCoordinate) ToChunkAddress(Int3 gridPosition)
    {
        var chunkX = DivFloor(gridPosition.X, ChunkResolution);
        var chunkY = DivFloor(gridPosition.Y, ChunkResolution);
        var chunkZ = DivFloor(gridPosition.Z, ChunkResolution);

        var localX = ModFloor(gridPosition.X, ChunkResolution);
        var localY = ModFloor(gridPosition.Y, ChunkResolution);
        var localZ = ModFloor(gridPosition.Z, ChunkResolution);

        return (new Int3(chunkX, chunkY, chunkZ), new Int3(localX, localY, localZ));
    }

    private static int DivFloor(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    private static int ModFloor(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static int FastFloor(float value)
    {
        var truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }
}
