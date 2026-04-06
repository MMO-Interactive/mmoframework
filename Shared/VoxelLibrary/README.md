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
