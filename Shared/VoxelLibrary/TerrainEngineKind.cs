namespace VoxelLibrary;

/// <summary>
/// Chooses how terrain density is produced for voxel chunks.
/// </summary>
public enum TerrainEngineKind
{
    /// <summary>
    /// Existing fully volumetric terrain behavior.
    /// </summary>
    Voxel = 0,

    /// <summary>
    /// Wurm-style tiled heightmap surface extruded into voxels.
    /// </summary>
    Tile = 1,

    /// <summary>
    /// Wurm-style tiled terrain with a voxel structure layer for building/construction.
    /// </summary>
    Hybrid = 2,
}
