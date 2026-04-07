using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGameEngine.Terrain;

public sealed class TileTerrain3D
{
    private readonly int _tilesX;
    private readonly int _tilesZ;
    private readonly float _tileSize;
    private readonly Func<float, float, float> _heightSampler;
    private readonly float _heightMultiplier;

    public TileTerrain3D(int tilesX, int tilesZ, float tileSize, Func<float, float, float> heightSampler, float heightMultiplier)
    {
        _tilesX = tilesX;
        _tilesZ = tilesZ;
        _tileSize = tileSize;
        _heightSampler = heightSampler;
        _heightMultiplier = heightMultiplier;
    }

    public TileTerrainMeshData BuildMesh()
    {
        var heights = GenerateCornerHeights();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();

        AddTopSurface(heights, vertices, indices);
        AddEdgeSkirts(heights, vertices, indices);

        return new TileTerrainMeshData(vertices.ToArray(), indices.ToArray());
    }

    private float[,] GenerateCornerHeights()
    {
        var heights = new float[_tilesX + 1, _tilesZ + 1];

        for (var x = 0; x <= _tilesX; x++)
        {
            for (var z = 0; z <= _tilesZ; z++)
            {
                var worldX = x * _tileSize;
                var worldZ = z * _tileSize;
                heights[x, z] = _heightSampler(worldX, worldZ) * _heightMultiplier;
            }
        }

        return heights;
    }

    private void AddTopSurface(float[,] heights, List<VertexPositionColor> vertices, List<int> indices)
    {
        for (var x = 0; x < _tilesX; x++)
        {
            for (var z = 0; z < _tilesZ; z++)
            {
                var v00 = new Vector3(x * _tileSize, heights[x, z], z * _tileSize);
                var v10 = new Vector3((x + 1) * _tileSize, heights[x + 1, z], z * _tileSize);
                var v01 = new Vector3(x * _tileSize, heights[x, z + 1], (z + 1) * _tileSize);
                var v11 = new Vector3((x + 1) * _tileSize, heights[x + 1, z + 1], (z + 1) * _tileSize);

                var avgHeight = (heights[x, z] + heights[x + 1, z] + heights[x, z + 1] + heights[x + 1, z + 1]) * 0.25f;
                var tileColor = HeightToColor(avgHeight);

                AddQuad(v00, v10, v11, v01, tileColor, vertices, indices);
            }
        }
    }

    private void AddEdgeSkirts(float[,] heights, List<VertexPositionColor> vertices, List<int> indices)
    {
        const float floor = 0f;
        var wallColor = new Color(35, 48, 40);

        for (var x = 0; x < _tilesX; x++)
        {
            var t0 = new Vector3(x * _tileSize, heights[x, 0], 0);
            var t1 = new Vector3((x + 1) * _tileSize, heights[x + 1, 0], 0);
            var b0 = new Vector3(t0.X, floor, 0);
            var b1 = new Vector3(t1.X, floor, 0);
            AddQuad(t1, t0, b0, b1, wallColor, vertices, indices);

            var zEdge = _tilesZ * _tileSize;
            var tb0 = new Vector3(x * _tileSize, heights[x, _tilesZ], zEdge);
            var tb1 = new Vector3((x + 1) * _tileSize, heights[x + 1, _tilesZ], zEdge);
            var bb0 = new Vector3(tb0.X, floor, zEdge);
            var bb1 = new Vector3(tb1.X, floor, zEdge);
            AddQuad(tb0, tb1, bb1, bb0, wallColor, vertices, indices);
        }

        for (var z = 0; z < _tilesZ; z++)
        {
            var t0 = new Vector3(0, heights[0, z], z * _tileSize);
            var t1 = new Vector3(0, heights[0, z + 1], (z + 1) * _tileSize);
            var b0 = new Vector3(0, floor, t0.Z);
            var b1 = new Vector3(0, floor, t1.Z);
            AddQuad(t0, t1, b1, b0, wallColor, vertices, indices);

            var xEdge = _tilesX * _tileSize;
            var tr0 = new Vector3(xEdge, heights[_tilesX, z], z * _tileSize);
            var tr1 = new Vector3(xEdge, heights[_tilesX, z + 1], (z + 1) * _tileSize);
            var br0 = new Vector3(xEdge, floor, tr0.Z);
            var br1 = new Vector3(xEdge, floor, tr1.Z);
            AddQuad(tr1, tr0, br0, br1, wallColor, vertices, indices);
        }
    }

    private static void AddQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color color,
        List<VertexPositionColor> vertices,
        List<int> indices)
    {
        var start = vertices.Count;
        vertices.Add(new VertexPositionColor(a, color));
        vertices.Add(new VertexPositionColor(b, color));
        vertices.Add(new VertexPositionColor(c, color));
        vertices.Add(new VertexPositionColor(d, color));

        indices.Add(start + 0);
        indices.Add(start + 1);
        indices.Add(start + 2);

        indices.Add(start + 0);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static Color HeightToColor(float height)
    {
        if (height < 4f)
        {
            return new Color(110, 140, 85);
        }

        if (height < 10f)
        {
            return new Color(88, 125, 70);
        }

        if (height < 18f)
        {
            return new Color(130, 120, 88);
        }

        return new Color(165, 165, 165);
    }
}

public sealed record TileTerrainMeshData(VertexPositionColor[] Vertices, int[] Indices);
