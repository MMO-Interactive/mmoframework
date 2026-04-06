namespace VoxelLibrary;

public readonly record struct VoxelSample(float Density, ushort MaterialId)
{
    public bool IsSolid(float isoLevel = 0f) => Density >= isoLevel;

    public static VoxelSample Air => new(-1f, 0);
}
