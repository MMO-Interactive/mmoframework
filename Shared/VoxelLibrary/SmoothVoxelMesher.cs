using System;

namespace VoxelLibrary;

public sealed class SmoothVoxelMesher
{
    private static readonly int[,] CubeCornerOffsets =
    {
        { 0, 0, 0 },
        { 1, 0, 0 },
        { 1, 1, 0 },
        { 0, 1, 0 },
        { 0, 0, 1 },
        { 1, 0, 1 },
        { 1, 1, 1 },
        { 0, 1, 1 }
    };

    private static readonly int[,] Tetrahedra =
    {
        { 0, 5, 1, 6 },
        { 0, 1, 2, 6 },
        { 0, 2, 3, 6 },
        { 0, 3, 7, 6 },
        { 0, 7, 4, 6 },
        { 0, 4, 5, 6 }
    };

    public VoxelMesh BuildMesh(VoxelChunk chunk, VoxelWorld world, float isoLevel = 0f)
    {
        var mesh = new VoxelMesh();
        AppendMesh(mesh, chunk, world, isoLevel);
        return mesh;
    }

    public void AppendMesh(VoxelMesh mesh, VoxelChunk chunk, VoxelWorld world, float isoLevel = 0f)
    {
        for (var z = 0; z < chunk.Resolution; z++)
        {
            for (var y = 0; y < chunk.Resolution; y++)
            {
                for (var x = 0; x < chunk.Resolution; x++)
                {
                    PolygonizeCube(mesh, chunk, world, x, y, z, isoLevel);
                }
            }
        }
    }

    private static void PolygonizeCube(VoxelMesh mesh, VoxelChunk chunk, VoxelWorld world, int x, int y, int z, float isoLevel)
    {
        var cubePoints = new GridPoint[8];
        for (var i = 0; i < 8; i++)
        {
            var sampleX = x + CubeCornerOffsets[i, 0];
            var sampleY = y + CubeCornerOffsets[i, 1];
            var sampleZ = z + CubeCornerOffsets[i, 2];
            cubePoints[i] = ReadGridPoint(chunk, world, sampleX, sampleY, sampleZ);
        }

        for (var i = 0; i < Tetrahedra.GetLength(0); i++)
        {
            var tetra = new[]
            {
                cubePoints[Tetrahedra[i, 0]],
                cubePoints[Tetrahedra[i, 1]],
                cubePoints[Tetrahedra[i, 2]],
                cubePoints[Tetrahedra[i, 3]]
            };

            PolygonizeTetrahedron(mesh, tetra, isoLevel);
        }
    }

    private static void PolygonizeTetrahedron(VoxelMesh mesh, GridPoint[] tetrahedron, float isoLevel)
    {
        Span<int> inside = stackalloc int[4];
        Span<int> outside = stackalloc int[4];
        var insideCount = 0;
        var outsideCount = 0;

        for (var i = 0; i < tetrahedron.Length; i++)
        {
            if (tetrahedron[i].Sample.Density >= isoLevel)
            {
                inside[insideCount++] = i;
            }
            else
            {
                outside[outsideCount++] = i;
            }
        }

        if (insideCount == 0 || insideCount == 4)
        {
            return;
        }

        if (insideCount == 1)
        {
            var a = CreateVertex(tetrahedron[inside[0]], tetrahedron[outside[0]], isoLevel);
            var b = CreateVertex(tetrahedron[inside[0]], tetrahedron[outside[1]], isoLevel);
            var c = CreateVertex(tetrahedron[inside[0]], tetrahedron[outside[2]], isoLevel);
            AddOrientedTriangle(mesh, a, b, c);
            return;
        }

        if (insideCount == 3)
        {
            var a = CreateVertex(tetrahedron[outside[0]], tetrahedron[inside[0]], isoLevel);
            var b = CreateVertex(tetrahedron[outside[0]], tetrahedron[inside[1]], isoLevel);
            var c = CreateVertex(tetrahedron[outside[0]], tetrahedron[inside[2]], isoLevel);
            AddOrientedTriangle(mesh, a, c, b);
            return;
        }

        var v0 = CreateVertex(tetrahedron[inside[0]], tetrahedron[outside[0]], isoLevel);
        var v1 = CreateVertex(tetrahedron[inside[0]], tetrahedron[outside[1]], isoLevel);
        var v2 = CreateVertex(tetrahedron[inside[1]], tetrahedron[outside[0]], isoLevel);
        var v3 = CreateVertex(tetrahedron[inside[1]], tetrahedron[outside[1]], isoLevel);

        AddOrientedTriangle(mesh, v0, v1, v2);
        AddOrientedTriangle(mesh, v1, v3, v2);
    }

    private static GridPoint ReadGridPoint(VoxelChunk chunk, VoxelWorld world, int localX, int localY, int localZ)
    {
        var worldGridX = (chunk.ChunkCoordinate.X * chunk.Resolution) + localX;
        var worldGridY = (chunk.ChunkCoordinate.Y * chunk.Resolution) + localY;
        var worldGridZ = (chunk.ChunkCoordinate.Z * chunk.Resolution) + localZ;

        var position = world.GridToWorldPosition(worldGridX, worldGridY, worldGridZ);
        var sample = world.GetSampleGlobal(worldGridX, worldGridY, worldGridZ);
        var normal = EstimateNormal(world, worldGridX, worldGridY, worldGridZ);
        return new GridPoint(position, sample, normal);
    }

    private static Float3 EstimateNormal(VoxelWorld world, int x, int y, int z)
    {
        var dx = world.GetSampleGlobal(x - 1, y, z).Density - world.GetSampleGlobal(x + 1, y, z).Density;
        var dy = world.GetSampleGlobal(x, y - 1, z).Density - world.GetSampleGlobal(x, y + 1, z).Density;
        var dz = world.GetSampleGlobal(x, y, z - 1).Density - world.GetSampleGlobal(x, y, z + 1).Density;
        return new Float3(dx, dy, dz).Normalized();
    }

    private static VoxelVertex CreateVertex(GridPoint inside, GridPoint outside, float isoLevel)
    {
        var denominator = outside.Sample.Density - inside.Sample.Density;
        var t = MathF.Abs(denominator) < 1e-6f ? 0.5f : (isoLevel - inside.Sample.Density) / denominator;
        t = Math.Clamp(t, 0f, 1f);

        var position = Float3.Lerp(inside.Position, outside.Position, t);
        var normal = Float3.Lerp(inside.Normal, outside.Normal, t).Normalized();
        if (normal.LengthSquared <= 1e-6f)
        {
            normal = Float3.Cross(outside.Position - inside.Position, new Float3(0f, 1f, 0f)).Normalized();
        }

        return new VoxelVertex(position, normal, ResolveMaterialId(inside.Sample, outside.Sample, isoLevel));
    }

    private static ushort ResolveMaterialId(VoxelSample first, VoxelSample second, float isoLevel)
    {
        if (first.IsSolid(isoLevel) && first.MaterialId != 0)
        {
            return first.MaterialId;
        }

        if (second.IsSolid(isoLevel) && second.MaterialId != 0)
        {
            return second.MaterialId;
        }

        if (first.MaterialId != 0)
        {
            return first.MaterialId;
        }

        if (second.MaterialId != 0)
        {
            return second.MaterialId;
        }

        return 1;
    }

    private static void AddOrientedTriangle(VoxelMesh mesh, in VoxelVertex a, in VoxelVertex b, in VoxelVertex c)
    {
        var faceNormal = Float3.Cross(b.Position - a.Position, c.Position - a.Position).Normalized();
        var expectedNormal = (a.Normal + b.Normal + c.Normal).Normalized();
        if (Float3.Dot(faceNormal, expectedNormal) < 0f)
        {
            mesh.AddTriangle(a, c, b);
            return;
        }

        mesh.AddTriangle(a, b, c);
    }

    private readonly record struct GridPoint(Float3 Position, VoxelSample Sample, Float3 Normal);
}
