# VoxelLibrary

`VoxelLibrary` is a renderer-agnostic smooth voxel package for C# and Unity workflows.

It does not reference `UnityEngine` or any rendering API. The output is plain mesh data:

- `VoxelVertex[]` for positions, normals, and material ids
- `int[]` for triangle indices

## Included

- Chunked voxel world storage with `VoxelWorld`
- Scalar samples with density and material id via `VoxelSample`
- Simple terrain brushes in `VoxelBrushes`
- Smooth surface meshing with `SmoothVoxelMesher`
- Deterministic chunk population for streaming terrain with `ProceduralVoxelWorldGenerator`

## Unity usage

Use the compiled DLL or copy the source files into a Unity project, then map the generated mesh into a Unity `Mesh` yourself.

```csharp
var world = new VoxelWorld(chunkResolution: 16, voxelSize: 1f);
VoxelBrushes.AddSphere(world, new Float3(8f, 8f, 8f), radius: 6f, materialId: 1);

var chunk = world.GetOrCreateChunk(Int3.Zero);
var mesher = new SmoothVoxelMesher();
var mesh = mesher.BuildMesh(chunk, world);

var vertices = mesh.ToVertexArray();
var indices = mesh.ToIndexArray();
```

That keeps this library independent from Unity while still making it easy to build a Unity adapter on top.


## Terrain engine options

`VoxelLibrary` now includes an `AdaptiveTerrainGenerator` that supports:

- `TerrainEngineKind.Voxel`: full volumetric terrain (best for caves/destructibility)
- `TerrainEngineKind.Tile`: Wurm-style tile map terrain with stepped tile corners
- `TerrainEngineKind.Hybrid`: Wurm-style tile terrain with a voxel building layer

For an MMORPG framework, **Hybrid** is the default recommendation when you want **Wurm-style tile terrain plus voxel buildings**: terrain comes from the tile map while player-made structures are authored as voxels on top.

```csharp
var terrain = new AdaptiveTerrainGenerator(
    engineKind: TerrainEngineKind.Hybrid,
    seed: 1337,
    tileSize: 4f,
    maxTileCornerStep: 3f);

// Optional: place voxel structures in world grid space.
terrain.AddStructureBox(new Int3(10, 14, 10), new Int3(14, 18, 14), materialId: 9);

var chunk = new VoxelChunk(Int3.Zero, resolution: 16, voxelSize: 1f);
terrain.PopulateChunk(chunk);
```
