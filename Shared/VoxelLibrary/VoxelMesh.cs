using System;
using System.Collections.Generic;

namespace VoxelLibrary;

public sealed class VoxelMesh
{
    private readonly List<VoxelVertex> _vertices = new();
    private readonly List<int> _indices = new();

    public IReadOnlyList<VoxelVertex> Vertices => _vertices;

    public IReadOnlyList<int> Indices => _indices;

    public bool IsEmpty => _indices.Count == 0;

    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }

    public void AddTriangle(in VoxelVertex a, in VoxelVertex b, in VoxelVertex c)
    {
        var baseIndex = _vertices.Count;
        _vertices.Add(a);
        _vertices.Add(b);
        _vertices.Add(c);
        _indices.Add(baseIndex);
        _indices.Add(baseIndex + 1);
        _indices.Add(baseIndex + 2);
    }

    public VoxelVertex[] ToVertexArray() => _vertices.ToArray();

    public int[] ToIndexArray() => _indices.ToArray();
}
