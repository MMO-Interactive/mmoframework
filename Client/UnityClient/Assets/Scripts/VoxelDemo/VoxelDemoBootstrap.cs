using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using VoxelLibrary;

public sealed class VoxelDemoBootstrap : MonoBehaviour
{
    [SerializeField] private int visibleChunkRadius = 5;
    [SerializeField] private int verticalChunkCount = 5;
    [SerializeField] private int detailChunkRadius = 2;
    [SerializeField] private int chunksPerBackgroundJob = 4;
    [SerializeField] private int maxBackgroundJobs = 2;
    [SerializeField] private int maxChunkCreatesPerFrame = 2;
    [SerializeField] private int maxChunkRemovalsPerFrame = 8;
    [SerializeField] private int colliderChunkRadius = 1;
    [SerializeField] private int maxColliderAssignmentsPerFrame = 1;
    [SerializeField] private Transform? viewer;

    private const int ChunkResolution = 16;
    private const float VoxelSize = 1f;
    private const int TerrainSeed = 1337;
    private const TerrainEngineKind TerrainEngine = TerrainEngineKind.Hybrid;
    private const float WaterLevel = ProceduralVoxelWorldGenerator.DefaultWaterLevel;

    private readonly Dictionary<Int3, PooledChunkVisual> _chunkVisuals = new();
    private readonly Queue<PooledChunkVisual> _chunkVisualPool = new();
    private readonly Queue<Int3> _pendingRemovals = new();
    private readonly HashSet<Int3> _pendingRemovalSet = new();
    private readonly Queue<ChunkRenderData> _pendingCreates = new();
    private readonly HashSet<Int3> _pendingCreateSet = new();
    private readonly Queue<Int3> _pendingColliderUpdates = new();
    private readonly HashSet<Int3> _pendingColliderUpdateSet = new();
    private readonly HashSet<Int3> _desiredVisibleCoordinates = new();
    private readonly Queue<Int3> _requestedChunks = new();
    private readonly HashSet<Int3> _queuedRequests = new();
    private readonly List<ChunkJobHandle> _activeJobs = new();
    private readonly HashSet<Int3> _runningJobs = new();
    private readonly ConcurrentDictionary<Int3, VoxelChunk> _chunkCache = new();
    private readonly ConcurrentDictionary<Int3, ChunkRenderData?> _meshCache = new();
    private readonly Dictionary<ushort, Material> _materialCache = new();
    private readonly AdaptiveTerrainGenerator _terrainGenerator = new(engineKind: TerrainEngine, seed: TerrainSeed);

    private Int3 _lastViewerChunk;
    private bool _hasViewerChunk;
    private int _requestVersion;

    private void Start()
    {
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }

        ForceRebuild();
    }

    private void Update()
    {
        if (viewer == null)
        {
            if (Camera.main == null)
            {
                return;
            }

            viewer = Camera.main.transform;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ForceRebuild();
        }

        var currentViewerChunk = GetViewerChunk(viewer.position);
        if (!_hasViewerChunk || currentViewerChunk != _lastViewerChunk)
        {
            _lastViewerChunk = currentViewerChunk;
            _hasViewerChunk = true;
            RefreshStreamingPlan(currentViewerChunk);
        }

        PollCompletedJobs();
        StartQueuedJobs();
        ProcessMainThreadQueues();
    }

    private void OnDestroy()
    {
        ClearGeneratedContent();

        foreach (var material in _materialCache.Values)
        {
            Destroy(material);
        }

        _materialCache.Clear();
    }

    private void ForceRebuild()
    {
        ClearGeneratedContent();
        _hasViewerChunk = false;

        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }

        if (viewer == null)
        {
            return;
        }

        _lastViewerChunk = GetViewerChunk(viewer.position);
        _hasViewerChunk = true;
        RefreshStreamingPlan(_lastViewerChunk);
    }

    private void RefreshStreamingPlan(Int3 viewerChunk)
    {
        _requestVersion++;
        _desiredVisibleCoordinates.Clear();

        var missingCoordinates = new List<Int3>();
        var safeVerticalCount = Mathf.Max(verticalChunkCount, 1);
        var safeRadius = Mathf.Max(visibleChunkRadius, 1);
        GetVerticalChunkRange(viewerChunk.Y, safeVerticalCount, out var firstChunkY, out var lastChunkY);

        for (var chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
        {
            for (var z = viewerChunk.Z - safeRadius; z <= viewerChunk.Z + safeRadius; z++)
            {
                for (var x = viewerChunk.X - safeRadius; x <= viewerChunk.X + safeRadius; x++)
                {
                    var coordinate = new Int3(x, chunkY, z);
                    _desiredVisibleCoordinates.Add(coordinate);

                    if (_meshCache.TryGetValue(coordinate, out var cachedRenderData))
                    {
                        var needsDetailedRender = RequiresDetailedRender(coordinate, viewerChunk);
                        if (cachedRenderData.HasValue
                            && (!needsDetailedRender || cachedRenderData.Value.HasDetailMeshes)
                            && !_chunkVisuals.ContainsKey(coordinate)
                            && !_pendingCreateSet.Contains(coordinate))
                        {
                            _pendingCreates.Enqueue(cachedRenderData.Value);
                            _pendingCreateSet.Add(coordinate);
                        }

                        continue;
                    }

                    if (_chunkVisuals.ContainsKey(coordinate)
                        || _pendingCreateSet.Contains(coordinate)
                        || _queuedRequests.Contains(coordinate)
                        || _runningJobs.Contains(coordinate))
                    {
                        continue;
                    }

                    missingCoordinates.Add(coordinate);
                }
            }
        }

        missingCoordinates.Sort((left, right) =>
        {
            var leftDistance = DistanceSquared(left, viewerChunk);
            var rightDistance = DistanceSquared(right, viewerChunk);
            return leftDistance.CompareTo(rightDistance);
        });

        foreach (var coordinate in missingCoordinates)
        {
            _requestedChunks.Enqueue(coordinate);
            _queuedRequests.Add(coordinate);
        }

        var removals = new List<Int3>();
        foreach (var coordinate in _chunkVisuals.Keys)
        {
            if (!_desiredVisibleCoordinates.Contains(coordinate))
            {
                removals.Add(coordinate);
            }
        }

        foreach (var coordinate in removals)
        {
            EnqueueRemoval(coordinate);
        }

        foreach (var coordinate in _chunkVisuals.Keys)
        {
            EnqueueColliderUpdate(coordinate);
        }
    }

    private void StartQueuedJobs()
    {
        var allowedJobs = Mathf.Max(maxBackgroundJobs, 1);
        while (_activeJobs.Count < allowedJobs && _requestedChunks.Count > 0)
        {
            var batchCoordinates = new List<Int3>(Mathf.Max(chunksPerBackgroundJob, 1));
            var desiredBatchSize = Mathf.Max(chunksPerBackgroundJob, 1);

            while (batchCoordinates.Count < desiredBatchSize && _requestedChunks.Count > 0)
            {
                var coordinate = _requestedChunks.Dequeue();
                _queuedRequests.Remove(coordinate);

                if (!_desiredVisibleCoordinates.Contains(coordinate) || _chunkVisuals.ContainsKey(coordinate))
                {
                    continue;
                }

                if (_meshCache.TryGetValue(coordinate, out var cachedRenderData))
                {
                    var needsDetailedRender = RequiresDetailedRender(coordinate, _lastViewerChunk);
                    if (cachedRenderData.HasValue
                        && (!needsDetailedRender || cachedRenderData.Value.HasDetailMeshes)
                        && _pendingCreateSet.Add(coordinate))
                    {
                        _pendingCreates.Enqueue(cachedRenderData.Value);
                    }

                    if (!needsDetailedRender || (cachedRenderData.HasValue && cachedRenderData.Value.HasDetailMeshes))
                    {
                        continue;
                    }
                }

                batchCoordinates.Add(coordinate);
            }

            if (batchCoordinates.Count == 0)
            {
                continue;
            }

            var version = _requestVersion;
            var verticalCount = Mathf.Max(verticalChunkCount, 1);
            GetVerticalChunkRange(_lastViewerChunk.Y, verticalCount, out var firstChunkY, out var lastChunkY);
            var viewerChunk = _lastViewerChunk;
            var task = Task.Run(() => GenerateChunkMeshBatch(batchCoordinates, viewerChunk, version, firstChunkY, lastChunkY));
            _activeJobs.Add(new ChunkJobHandle(batchCoordinates.ToArray(), version, task));
            foreach (var coordinate in batchCoordinates)
            {
                _runningJobs.Add(coordinate);
            }
        }
    }

    private void PollCompletedJobs()
    {
        for (var i = _activeJobs.Count - 1; i >= 0; i--)
        {
            var handle = _activeJobs[i];
            if (!handle.Task.IsCompleted)
            {
                continue;
            }

            _activeJobs.RemoveAt(i);
            foreach (var coordinate in handle.Coordinates)
            {
                _runningJobs.Remove(coordinate);
            }

            IReadOnlyList<ChunkJobResult> results;
            try
            {
                results = handle.Task.Result;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                continue;
            }

            foreach (var result in results)
            {
                if (result.Version != _requestVersion || !_desiredVisibleCoordinates.Contains(result.Coordinate))
                {
                    if (_desiredVisibleCoordinates.Contains(result.Coordinate)
                        && !_chunkVisuals.ContainsKey(result.Coordinate)
                        && !_pendingCreateSet.Contains(result.Coordinate)
                        && !_queuedRequests.Contains(result.Coordinate))
                    {
                        _requestedChunks.Enqueue(result.Coordinate);
                        _queuedRequests.Add(result.Coordinate);
                    }

                    continue;
                }

                _meshCache[result.Coordinate] = result.RenderData;

                if (result.RenderData == null)
                {
                    continue;
                }

                if (_pendingCreateSet.Add(result.Coordinate))
                {
                    _pendingCreates.Enqueue(result.RenderData.Value);
                }
            }
        }
    }

    private void ProcessMainThreadQueues()
    {
        var removalsThisFrame = Mathf.Max(maxChunkRemovalsPerFrame, 0);
        for (var i = 0; i < removalsThisFrame && _pendingRemovals.Count > 0; i++)
        {
            var coordinate = _pendingRemovals.Dequeue();
            _pendingRemovalSet.Remove(coordinate);
            DestroyChunkVisual(coordinate);
        }

        var createsThisFrame = Mathf.Max(maxChunkCreatesPerFrame, 0);
        for (var i = 0; i < createsThisFrame && _pendingCreates.Count > 0; i++)
        {
            var renderData = _pendingCreates.Dequeue();
            _pendingCreateSet.Remove(renderData.Coordinate);

            if (!_desiredVisibleCoordinates.Contains(renderData.Coordinate))
            {
                continue;
            }

            ApplyChunkVisual(renderData);
        }

        var colliderUpdatesThisFrame = Mathf.Max(maxColliderAssignmentsPerFrame, 0);
        for (var i = 0; i < colliderUpdatesThisFrame && _pendingColliderUpdates.Count > 0; i++)
        {
            var coordinate = _pendingColliderUpdates.Dequeue();
            _pendingColliderUpdateSet.Remove(coordinate);
            RefreshChunkCollider(coordinate);
        }
    }

    private void ApplyChunkVisual(ChunkRenderData renderData)
    {
        DestroyChunkVisual(renderData.Coordinate);

        var visual = AcquireChunkVisual();
        visual.Root.name = $"Voxel Chunk {renderData.Coordinate.X},{renderData.Coordinate.Y},{renderData.Coordinate.Z}";
        visual.Root.transform.SetParent(transform, false);
        visual.Root.SetActive(true);

        if (visual.TerrainCollider != null)
        {
            visual.TerrainCollider.sharedMesh = null;
        }

        visual.TerrainMesh = CreateOrUpdateUnityMesh(visual.TerrainMesh, $"{visual.Root.name} Terrain", renderData.TerrainMesh);
        visual.TerrainFilter.sharedMesh = visual.TerrainMesh;
        visual.TerrainRenderer.sharedMaterials = GetOrCreateMaterialsForMesh(renderData.TerrainMesh);

        if (renderData.TreeMesh.HasValue)
        {
            visual.TreeObject.SetActive(true);
            visual.TreeMesh = CreateOrUpdateUnityMesh(visual.TreeMesh, $"{visual.Root.name} Trees", renderData.TreeMesh.Value);
            visual.TreeFilter.sharedMesh = visual.TreeMesh;
            visual.TreeRenderer.sharedMaterials = GetOrCreateMaterialsForMesh(renderData.TreeMesh.Value);
        }
        else
        {
            ClearUnityMesh(visual.TreeMesh);
            visual.TreeFilter.sharedMesh = null;
            visual.TreeRenderer.sharedMaterials = Array.Empty<Material>();
            visual.TreeObject.SetActive(false);
        }

        if (renderData.WaterMesh.HasValue)
        {
            visual.WaterObject.SetActive(true);
            visual.WaterMesh = CreateOrUpdateUnityMesh(visual.WaterMesh, $"{visual.Root.name} Water", renderData.WaterMesh.Value);
            visual.WaterFilter.sharedMesh = visual.WaterMesh;
            visual.WaterRenderer.sharedMaterials = GetOrCreateMaterialsForMesh(renderData.WaterMesh.Value);
        }
        else
        {
            ClearUnityMesh(visual.WaterMesh);
            visual.WaterFilter.sharedMesh = null;
            visual.WaterRenderer.sharedMaterials = Array.Empty<Material>();
            visual.WaterObject.SetActive(false);
        }

        _chunkVisuals[renderData.Coordinate] = visual;
        EnqueueColliderUpdate(renderData.Coordinate);
    }

    private PooledChunkVisual AcquireChunkVisual()
    {
        if (_chunkVisualPool.Count > 0)
        {
            return _chunkVisualPool.Dequeue();
        }

        var root = new GameObject("Voxel Chunk");
        var terrainFilter = root.AddComponent<MeshFilter>();
        var terrainRenderer = root.AddComponent<MeshRenderer>();

        var treeObject = new GameObject("Trees");
        treeObject.transform.SetParent(root.transform, false);
        var treeFilter = treeObject.AddComponent<MeshFilter>();
        var treeRenderer = treeObject.AddComponent<MeshRenderer>();

        var waterObject = new GameObject("Water");
        waterObject.transform.SetParent(root.transform, false);
        var waterFilter = waterObject.AddComponent<MeshFilter>();
        var waterRenderer = waterObject.AddComponent<MeshRenderer>();
        waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        waterRenderer.receiveShadows = false;

        return new PooledChunkVisual(root, terrainFilter, terrainRenderer, treeObject, treeFilter, treeRenderer, waterObject, waterFilter, waterRenderer);
    }

    private void EnqueueRemoval(Int3 coordinate)
    {
        if (_pendingRemovalSet.Add(coordinate))
        {
            _pendingRemovals.Enqueue(coordinate);
        }
    }

    private void EnqueueColliderUpdate(Int3 coordinate)
    {
        if (_pendingColliderUpdateSet.Add(coordinate))
        {
            _pendingColliderUpdates.Enqueue(coordinate);
        }
    }

    private void RefreshChunkCollider(Int3 coordinate)
    {
        if (!_chunkVisuals.TryGetValue(coordinate, out var visual) || visual.TerrainMesh == null)
        {
            return;
        }

        if (!ShouldHaveCollider(coordinate))
        {
            if (visual.TerrainCollider != null)
            {
                Destroy(visual.TerrainCollider);
                visual.TerrainCollider = null;
            }

            return;
        }

        var terrainCollider = visual.TerrainCollider;
        if (terrainCollider == null)
        {
            terrainCollider = visual.Root.AddComponent<MeshCollider>();
            visual.TerrainCollider = terrainCollider;
        }

        if (terrainCollider.sharedMesh != visual.TerrainMesh)
        {
            terrainCollider.sharedMesh = null;
            terrainCollider.sharedMesh = visual.TerrainMesh;
        }
    }

    private bool ShouldHaveCollider(Int3 coordinate)
    {
        var radius = Mathf.Max(colliderChunkRadius, 0);
        return Math.Abs(coordinate.X - _lastViewerChunk.X) <= radius
            && Math.Abs(coordinate.Y - _lastViewerChunk.Y) <= 1
            && Math.Abs(coordinate.Z - _lastViewerChunk.Z) <= radius;
    }

    private bool RequiresDetailedRender(Int3 coordinate, Int3 viewerChunk)
    {
        var radius = Mathf.Max(detailChunkRadius, 0);
        return Math.Abs(coordinate.X - viewerChunk.X) <= radius
            && Math.Abs(coordinate.Y - viewerChunk.Y) <= 1
            && Math.Abs(coordinate.Z - viewerChunk.Z) <= radius;
    }

    private void DestroyChunkVisual(Int3 coordinate)
    {
        if (!_chunkVisuals.TryGetValue(coordinate, out var visual))
        {
            return;
        }

        _chunkVisuals.Remove(coordinate);
        _pendingColliderUpdateSet.Remove(coordinate);
        RecycleChunkVisual(visual);
    }

    private Int3 GetViewerChunk(Vector3 position)
    {
        var gridX = FloorDiv(WorldToGrid(position.x), ChunkResolution);
        var gridY = FloorDiv(WorldToGrid(position.y), ChunkResolution);
        var gridZ = FloorDiv(WorldToGrid(position.z), ChunkResolution);
        return new Int3(gridX, gridY, gridZ);
    }

    private void ClearGeneratedContent()
    {
        foreach (var visual in _chunkVisuals.Values)
        {
            DestroyPooledChunkVisual(visual);
        }

        _chunkVisuals.Clear();
        while (_chunkVisualPool.Count > 0)
        {
            DestroyPooledChunkVisual(_chunkVisualPool.Dequeue());
        }
        _pendingCreates.Clear();
        _pendingCreateSet.Clear();
        _pendingColliderUpdates.Clear();
        _pendingColliderUpdateSet.Clear();
        _pendingRemovals.Clear();
        _pendingRemovalSet.Clear();
        _desiredVisibleCoordinates.Clear();
        _requestedChunks.Clear();
        _queuedRequests.Clear();
        _activeJobs.Clear();
        _runningJobs.Clear();
        _meshCache.Clear();
        _chunkCache.Clear();
    }

    private void RecycleChunkVisual(PooledChunkVisual visual)
    {
        if (visual.TerrainCollider != null)
        {
            visual.TerrainCollider.sharedMesh = null;
        }

        ClearUnityMesh(visual.TerrainMesh);
        visual.TerrainFilter.sharedMesh = null;
        visual.TerrainRenderer.sharedMaterials = Array.Empty<Material>();

        ClearUnityMesh(visual.TreeMesh);
        visual.TreeFilter.sharedMesh = null;
        visual.TreeRenderer.sharedMaterials = Array.Empty<Material>();
        visual.TreeObject.SetActive(false);

        ClearUnityMesh(visual.WaterMesh);
        visual.WaterFilter.sharedMesh = null;
        visual.WaterRenderer.sharedMaterials = Array.Empty<Material>();
        visual.WaterObject.SetActive(false);

        visual.Root.SetActive(false);
        visual.Root.transform.SetParent(transform, false);
        _chunkVisualPool.Enqueue(visual);
    }

    private static void ClearUnityMesh(Mesh? mesh)
    {
        mesh?.Clear(false);
    }

    private static Mesh? ReleaseMesh(Mesh? mesh)
    {
        if (mesh == null)
        {
            return null;
        }

        UnityEngine.Object.Destroy(mesh);
        return null;
    }

    private static void DestroyPooledChunkVisual(PooledChunkVisual visual)
    {
        visual.TerrainMesh = ReleaseMesh(visual.TerrainMesh);
        visual.TreeMesh = ReleaseMesh(visual.TreeMesh);
        visual.WaterMesh = ReleaseMesh(visual.WaterMesh);

        if (visual.Root != null)
        {
            UnityEngine.Object.Destroy(visual.Root);
        }
    }

    private IReadOnlyList<ChunkJobResult> GenerateChunkMeshBatch(IReadOnlyList<Int3> coordinates, Int3 viewerChunk, int version, int firstChunkY, int lastChunkY)
    {
        var world = new VoxelWorld(ChunkResolution, VoxelSize);
        var mesher = new SmoothVoxelMesher();
        var generator = _terrainGenerator;
        var loadedSupportCoordinates = new HashSet<Int3>();

        for (var i = 0; i < coordinates.Count; i++)
        {
            var coordinate = coordinates[i];
            var minChunkY = Mathf.Max(coordinate.Y - 1, firstChunkY);
            var maxChunkY = Mathf.Min(coordinate.Y + 1, lastChunkY);

            for (var chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                for (var z = coordinate.Z - 1; z <= coordinate.Z + 1; z++)
                {
                    for (var x = coordinate.X - 1; x <= coordinate.X + 1; x++)
                    {
                        var supportCoordinate = new Int3(x, chunkY, z);
                        if (!loadedSupportCoordinates.Add(supportCoordinate))
                        {
                            continue;
                        }

                        var supportChunk = GetOrCreateCachedChunk(supportCoordinate);
                        world.AddOrReplaceChunk(supportChunk);
                    }
                }
            }
        }

        var results = new ChunkJobResult[coordinates.Count];
        for (var i = 0; i < coordinates.Count; i++)
        {
            results[i] = GenerateChunkMeshData(coordinates[i], viewerChunk, version, world, mesher, generator);
        }

        return results;
    }

    private ChunkJobResult GenerateChunkMeshData(
        Int3 coordinate,
        Int3 viewerChunk,
        int version,
        VoxelWorld world,
        SmoothVoxelMesher mesher,
        AdaptiveTerrainGenerator generator)
    {
        var generateDetailMeshes = RequiresDetailedRender(coordinate, viewerChunk);

        if (!world.TryGetChunk(coordinate, out var targetChunk) || targetChunk == null)
        {
            return new ChunkJobResult(coordinate, version, null);
        }

        var voxelMesh = mesher.BuildMesh(targetChunk, world);
        if (voxelMesh.IsEmpty)
        {
            return new ChunkJobResult(coordinate, version, null);
        }

        var environmentData = BuildChunkEnvironmentData(coordinate, generator);
        var terrainMesh = BuildTerrainMeshData(voxelMesh.ToVertexArray(), voxelMesh.ToIndexArray(), environmentData);
        var treeMesh = generateDetailMeshes ? BuildTreeMeshData(coordinate, environmentData) : null;
        var waterMesh = BuildWaterMeshData(coordinate, environmentData);
        var renderData = new ChunkRenderData(coordinate, terrainMesh, treeMesh, waterMesh, generateDetailMeshes);
        return new ChunkJobResult(coordinate, version, renderData);
    }

    private VoxelChunk GetOrCreateCachedChunk(Int3 coordinate)
    {
        return _chunkCache.GetOrAdd(coordinate, chunkCoordinate =>
        {
            var chunk = new VoxelChunk(chunkCoordinate, ChunkResolution, VoxelSize);
            _terrainGenerator.PopulateChunk(chunk);
            return chunk;
        });
    }

    private static int DistanceSquared(Int3 left, Int3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static int WorldToGrid(float position)
    {
        var scaled = position / VoxelSize;
        var truncated = (int)scaled;
        return scaled < truncated ? truncated - 1 : truncated;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        if (remainder != 0 && value < 0)
        {
            quotient--;
        }

        return quotient;
    }

    private static void GetVerticalChunkRange(int centerChunkY, int count, out int firstChunkY, out int lastChunkY)
    {
        firstChunkY = centerChunkY - (count / 2);
        lastChunkY = firstChunkY + count - 1;
    }

    private static MeshBuildData BuildTerrainMeshData(VoxelVertex[] sourceVertices, int[] indices, ChunkEnvironmentData environmentData)
    {
        var classifiedVertices = new VoxelVertex[sourceVertices.Length];
        for (var i = 0; i < sourceVertices.Length; i++)
        {
            var vertex = sourceVertices[i];
            var materialId = ClassifyTerrainMaterial(vertex.Position, vertex.Normal, environmentData);
            classifiedVertices[i] = new VoxelVertex(vertex.Position, vertex.Normal, materialId);
        }

        return new MeshBuildData(classifiedVertices, indices);
    }

    private static ushort ClassifyTerrainMaterial(Float3 position, Float3 normal, ChunkEnvironmentData environmentData)
    {
        var terrainHeight = environmentData.SampleTerrainHeight(position.X, position.Z);
        var depthBelowSurface = terrainHeight - position.Y;
        var upDot = normal.Y;
        var riverMask = environmentData.SampleRiverMask(position.X, position.Z);
        var cliffMask = environmentData.SampleCliffMask(position.X, position.Z);
        var outcropMask = environmentData.SampleOutcropMask(position.X, position.Z);
        var highlandMask = environmentData.SampleHighlandMask(position.X, position.Z);
        var erosionMask = environmentData.SampleErosionMask(position.X, position.Z);
        var marshMask = environmentData.SampleMarshMask(position.X, position.Z);
        var waterSurfaceHeight = environmentData.SampleWaterSurfaceHeight(position.X, position.Z);
        var moisture = environmentData.SampleMoisture(position.X, position.Z);
        var temperature = environmentData.SampleTemperature(position.X, position.Z);
        var rockyMask = environmentData.SampleRockyMask(position.X, position.Z);
        var alpineLine = WaterLevel + 12f + (highlandMask * 8f) - (temperature * 5f);

        if (!float.IsNegativeInfinity(waterSurfaceHeight) && position.Y <= waterSurfaceHeight + 0.8f)
        {
            return 4;
        }

        if (position.Y >= alpineLine && temperature < 0.48f && upDot > 0.42f)
        {
            return 8;
        }

        if ((riverMask > 0.48f || marshMask > 0.54f) && upDot > 0.58f && depthBelowSurface < 3.5f)
        {
            return 4;
        }

        if (upDot < 0.45f || rockyMask > 0.72f || outcropMask > 0.6f || riverMask > 0.72f || cliffMask > 0.45f)
        {
            return 3;
        }

        if (depthBelowSurface > 2.2f || erosionMask > 0.64f || (moisture < 0.38f && temperature > 0.58f))
        {
            return 1;
        }

        if (position.Y < WaterLevel + 2.5f && moisture < 0.45f)
        {
            return 4;
        }

        return 2;
    }

    private static Mesh CreateOrUpdateUnityMesh(Mesh? mesh, string name, MeshBuildData meshData)
    {
        var submeshLookup = new Dictionary<ushort, List<int>>();
        mesh ??= new Mesh();
        mesh.Clear(false);
        mesh.name = name;
        mesh.indexFormat = meshData.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        var unityVertices = new Vector3[meshData.Vertices.Length];
        var unityNormals = new Vector3[meshData.Vertices.Length];
        for (var i = 0; i < meshData.Vertices.Length; i++)
        {
            var vertex = meshData.Vertices[i];
            unityVertices[i] = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            unityNormals[i] = new Vector3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
        }

        mesh.vertices = unityVertices;
        mesh.normals = unityNormals;
        for (var i = 0; i < meshData.Indices.Length; i += 3)
        {
            var materialId = meshData.Vertices[meshData.Indices[i]].MaterialId;
            if (!submeshLookup.TryGetValue(materialId, out var triangleList))
            {
                triangleList = new List<int>();
                submeshLookup.Add(materialId, triangleList);
            }

            triangleList.Add(meshData.Indices[i]);
            triangleList.Add(meshData.Indices[i + 1]);
            triangleList.Add(meshData.Indices[i + 2]);
        }

        mesh.subMeshCount = submeshLookup.Count;
        var submeshIndex = 0;
        foreach (var triangles in submeshLookup.Values)
        {
            mesh.SetTriangles(triangles, submeshIndex++);
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material[] GetOrCreateMaterialsForMesh(MeshBuildData meshData)
    {
        var orderedMaterialIds = new List<ushort>();
        var seen = new HashSet<ushort>();

        for (var i = 0; i < meshData.Indices.Length; i += 3)
        {
            var materialId = meshData.Vertices[meshData.Indices[i]].MaterialId;
            if (seen.Add(materialId))
            {
                orderedMaterialIds.Add(materialId);
            }
        }

        var materials = new Material[orderedMaterialIds.Count];
        for (var i = 0; i < orderedMaterialIds.Count; i++)
        {
            materials[i] = GetOrCreateMaterial(orderedMaterialIds[i]);
        }

        return materials;
    }

    private Material GetOrCreateMaterial(ushort materialId)
    {
        if (_materialCache.TryGetValue(materialId, out var existing))
        {
            return existing;
        }

        var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
        if (shader == null)
        {
            throw new MissingReferenceException("Could not find a built-in shader for the voxel demo material.");
        }

        var material = new Material(shader)
        {
            name = $"Voxel Material {materialId}",
            color = GetMaterialColor(materialId)
        };
        material.mainTexture = GetTextureForMaterial(materialId);
        ConfigureMaterial(material, materialId);

        _materialCache.Add(materialId, material);
        return material;
    }

    private static Color GetMaterialColor(ushort materialId)
    {
        return materialId switch
        {
            1 => new Color(0.44f, 0.33f, 0.22f, 1f),
            2 => new Color(0.47f, 0.72f, 0.34f, 1f),
            3 => new Color(0.48f, 0.48f, 0.5f, 1f),
            4 => new Color(0.71f, 0.68f, 0.59f, 1f),
            5 => new Color(0.36f, 0.24f, 0.14f, 1f),
            6 => new Color(0.18f, 0.42f, 0.16f, 1f),
            7 => new Color(0.18f, 0.46f, 0.78f, 0.82f),
            8 => new Color(0.9f, 0.92f, 0.96f, 1f),
            _ => new Color(0.8f, 0.2f, 0.8f, 1f)
        };
    }

    private static Texture2D? GetTextureForMaterial(ushort materialId)
    {
        var resourceName = materialId switch
        {
            1 => "VoxelDemo/Textures/Dirt",
            2 => "VoxelDemo/Textures/Grass",
            3 => "VoxelDemo/Textures/Rock",
            4 => "VoxelDemo/Textures/Sand",
            5 => "VoxelDemo/Textures/Trunk",
            6 => "VoxelDemo/Textures/Leaves",
            7 => null,
            8 => null,
            _ => "VoxelDemo/Textures/Fallback"
        };

        if (string.IsNullOrEmpty(resourceName))
        {
            return null;
        }

        return Resources.Load<Texture2D>(resourceName);
    }

    private static void ConfigureMaterial(Material material, ushort materialId)
    {
        if (materialId != 7)
        {
            return;
        }

        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetFloat("_Glossiness", 0.82f);
        material.SetFloat("_Metallic", 0.02f);
    }

    private static ChunkEnvironmentData BuildChunkEnvironmentData(Int3 coordinate, AdaptiveTerrainGenerator generator)
    {
        var sampleCount = ChunkResolution + 3;
        var originX = (coordinate.X * ChunkResolution) - 1f;
        var originZ = (coordinate.Z * ChunkResolution) - 1f;

        var terrainHeight = new float[sampleCount, sampleCount];
        var waterHeight = new float[sampleCount, sampleCount];
        var waterValid = new bool[sampleCount, sampleCount];
        var riverMask = new float[sampleCount, sampleCount];
        var cliffMask = new float[sampleCount, sampleCount];
        var outcropMask = new float[sampleCount, sampleCount];
        var highlandMask = new float[sampleCount, sampleCount];
        var erosionMask = new float[sampleCount, sampleCount];
        var marshMask = new float[sampleCount, sampleCount];
        var moisture = new float[sampleCount, sampleCount];
        var temperature = new float[sampleCount, sampleCount];
        var forestDensity = new float[sampleCount, sampleCount];
        var rockyMask = new float[sampleCount, sampleCount];
        var basinNoise = new float[sampleCount, sampleCount];
        var localMoistureNoise = new float[sampleCount, sampleCount];

        for (var z = 0; z < sampleCount; z++)
        {
            for (var x = 0; x < sampleCount; x++)
            {
                var sampleX = originX + x;
                var sampleZ = originZ + z;
                terrainHeight[x, z] = generator.GetTerrainHeight(sampleX, sampleZ);
                riverMask[x, z] = generator.GetRiverMask(sampleX, sampleZ);
                cliffMask[x, z] = generator.GetCliffMask(sampleX, sampleZ);
                outcropMask[x, z] = generator.GetOutcropMask(sampleX, sampleZ);
                highlandMask[x, z] = generator.GetHighlandMask(sampleX, sampleZ);
                erosionMask[x, z] = generator.GetErosionMask(sampleX, sampleZ);
                marshMask[x, z] = generator.GetMarshMask(sampleX, sampleZ);
                moisture[x, z] = generator.GetMoisture(sampleX, sampleZ);
                temperature[x, z] = generator.GetTemperature(sampleX, sampleZ);
                forestDensity[x, z] = generator.GetForestDensity(sampleX, sampleZ);
                rockyMask[x, z] = SampleBiomeNoise(sampleX * 0.035f, sampleZ * 0.035f, 47);
                basinNoise[x, z] = SampleBiomeNoise(sampleX * 0.03f, sampleZ * 0.03f, 203);
                localMoistureNoise[x, z] = SampleBiomeNoise(sampleX * 0.016f, sampleZ * 0.016f, 211);

                var sampledWaterHeight = generator.GetWaterSurfaceHeight(sampleX, sampleZ);
                if (!float.IsNegativeInfinity(sampledWaterHeight))
                {
                    waterHeight[x, z] = sampledWaterHeight;
                    waterValid[x, z] = true;
                }
            }
        }

        return new ChunkEnvironmentData(
            originX,
            originZ,
            sampleCount,
            terrainHeight,
            waterHeight,
            waterValid,
            riverMask,
            cliffMask,
            outcropMask,
            highlandMask,
            erosionMask,
            marshMask,
            moisture,
            temperature,
            forestDensity,
            rockyMask,
            basinNoise,
            localMoistureNoise);
    }

    private MeshBuildData? BuildTreeMeshData(Int3 coordinate, ChunkEnvironmentData environmentData)
    {
        var chunkMinX = coordinate.X * ChunkResolution;
        var chunkMinZ = coordinate.Z * ChunkResolution;
        var chunkMinY = coordinate.Y * ChunkResolution;
        var chunkMaxY = chunkMinY + ChunkResolution;

        var vertices = new List<VoxelVertex>();
        var indices = new List<int>();

        for (var cellZ = 2; cellZ < ChunkResolution - 2; cellZ += 6)
        {
            for (var cellX = 2; cellX < ChunkResolution - 2; cellX += 6)
            {
                var worldX = chunkMinX + cellX;
                var worldZ = chunkMinZ + cellZ;
                var sampleX = worldX + 0.5f;
                var sampleZ = worldZ + 0.5f;
                var riverMask = environmentData.SampleRiverMask(sampleX, sampleZ);
                var cliffMask = environmentData.SampleCliffMask(sampleX, sampleZ);
                var outcropMask = environmentData.SampleOutcropMask(sampleX, sampleZ);
                var forestDensity = environmentData.SampleForestDensity(sampleX, sampleZ);
                var marshMask = environmentData.SampleMarshMask(sampleX, sampleZ);
                var highlandMask = environmentData.SampleHighlandMask(sampleX, sampleZ);
                var moisture = environmentData.SampleMoisture(sampleX, sampleZ);
                var temperature = environmentData.SampleTemperature(sampleX, sampleZ);
                var terrainHeight = environmentData.SampleTerrainHeight(sampleX, sampleZ);
                if (terrainHeight < chunkMinY || terrainHeight >= chunkMaxY)
                {
                    continue;
                }

                var slopeX = Math.Abs(environmentData.SampleTerrainHeight(sampleX + 1f, sampleZ) - terrainHeight);
                var slopeZ = Math.Abs(environmentData.SampleTerrainHeight(sampleX, sampleZ + 1f) - terrainHeight);
                if (slopeX + slopeZ > 1.1f)
                {
                    continue;
                }

                var chance = Deterministic01(worldX, coordinate.Y, worldZ, 91);
                var trunkHeight = 4.5f + (Deterministic01(worldX, coordinate.Y, worldZ, 17) * 3.5f);
                var canopyHeight = 3.2f + (Deterministic01(worldX, coordinate.Y, worldZ, 33) * 1.8f);
                var canopyRadius = 1.1f + (Deterministic01(worldX, coordinate.Y, worldZ, 47) * 0.75f);
                var leanX = (DeterministicSigned(worldX, coordinate.Y, worldZ, 101) * 0.18f);
                var leanZ = (DeterministicSigned(worldX, coordinate.Y, worldZ, 131) * 0.18f);
                var basePosition = new Float3(worldX + 0.5f, terrainHeight + 0.15f, worldZ + 0.5f);
                var trunkTop = basePosition + new Float3(leanX, trunkHeight, leanZ);

                if (outcropMask > 0.58f && chance > 0.42f)
                {
                    var boulderHeight = 0.55f + (Deterministic01(worldX, coordinate.Y, worldZ, 211) * 0.8f);
                    var boulderRadius = 0.55f + (Deterministic01(worldX, coordinate.Y, worldZ, 223) * 0.65f);
                    AppendBox(vertices, indices, basePosition + new Float3(0f, boulderHeight * 0.5f, 0f), new Float3(boulderRadius, boulderHeight * 0.5f, boulderRadius * 0.82f), 3);
                    continue;
                }

                if (riverMask > 0.34f || cliffMask > 0.4f || highlandMask > 0.84f)
                {
                    if (marshMask > 0.52f && chance > 0.58f)
                    {
                        AppendBox(vertices, indices, basePosition + new Float3(0f, 0.35f, 0f), new Float3(0.55f, 0.35f, 0.55f), 6);
                    }

                    continue;
                }

                if (forestDensity > 0.6f && moisture > 0.48f && temperature < 0.72f && chance > 0.68f)
                {
                    AppendBox(vertices, indices, basePosition + new Float3(leanX * 0.5f, trunkHeight * 0.5f, leanZ * 0.5f), new Float3(0.18f, trunkHeight * 0.5f, 0.18f), 5);
                    AppendBox(vertices, indices, trunkTop + new Float3(0f, canopyHeight * 0.18f, 0f), new Float3(canopyRadius * 0.6f, canopyHeight * 0.18f, canopyRadius * 0.6f), 6);
                    AppendBox(vertices, indices, trunkTop + new Float3(0f, canopyHeight * 0.48f, 0f), new Float3(canopyRadius, canopyHeight * 0.22f, canopyRadius), 6);
                    AppendBox(vertices, indices, trunkTop + new Float3(0f, canopyHeight * 0.78f, 0f), new Float3(canopyRadius * 0.72f, canopyHeight * 0.18f, canopyRadius * 0.72f), 6);
                    continue;
                }

                if ((forestDensity > 0.42f || marshMask > 0.48f) && chance > 0.52f)
                {
                    var shrubHeight = 0.5f + (Deterministic01(worldX, coordinate.Y, worldZ, 307) * 0.45f);
                    var shrubRadius = 0.45f + (Deterministic01(worldX, coordinate.Y, worldZ, 317) * 0.32f);
                    AppendBox(vertices, indices, basePosition + new Float3(0f, shrubHeight * 0.4f, 0f), new Float3(shrubRadius * 0.7f, shrubHeight * 0.26f, shrubRadius * 0.7f), 6);
                    AppendBox(vertices, indices, basePosition + new Float3(0f, shrubHeight * 0.8f, 0f), new Float3(shrubRadius, shrubHeight * 0.3f, shrubRadius), 6);
                }
            }
        }

        return vertices.Count == 0 ? null : new MeshBuildData(vertices.ToArray(), indices.ToArray());
    }

    private MeshBuildData? BuildWaterMeshData(Int3 coordinate, ChunkEnvironmentData environmentData)
    {
        var chunkMinX = coordinate.X * ChunkResolution;
        var chunkMinZ = coordinate.Z * ChunkResolution;
        var chunkMinY = coordinate.Y * ChunkResolution;
        var chunkMaxY = chunkMinY + ChunkResolution;

        var vertices = new List<VoxelVertex>();
        var indices = new List<int>();
        var waterMask = new bool[ChunkResolution, ChunkResolution];
        var waterHeights = new float[ChunkResolution, ChunkResolution];

        for (var z = 0; z < ChunkResolution; z++)
        {
            for (var x = 0; x < ChunkResolution; x++)
            {
                var worldX = chunkMinX + x;
                var worldZ = chunkMinZ + z;
                var sampleX = worldX + 0.5f;
                var sampleZ = worldZ + 0.5f;
                var terrainHeight = environmentData.SampleTerrainHeight(sampleX, sampleZ);
                var waterSurfaceHeight = environmentData.SampleWaterSurfaceHeight(sampleX, sampleZ);
                var marshMask = environmentData.SampleMarshMask(sampleX, sampleZ);
                var outcropMask = environmentData.SampleOutcropMask(sampleX, sampleZ);
                if (float.IsNegativeInfinity(waterSurfaceHeight))
                {
                    continue;
                }

                if (waterSurfaceHeight < chunkMinY || waterSurfaceHeight >= chunkMaxY)
                {
                    continue;
                }

                if (terrainHeight >= waterSurfaceHeight - 1.1f)
                {
                    continue;
                }

                var slopeX = Math.Abs(environmentData.SampleTerrainHeight(sampleX + 1f, sampleZ) - terrainHeight);
                var slopeZ = Math.Abs(environmentData.SampleTerrainHeight(sampleX, sampleZ + 1f) - terrainHeight);
                if ((slopeX + slopeZ) > 1.65f)
                {
                    continue;
                }

                var basinNoise = environmentData.SampleBasinNoise(sampleX, sampleZ);
                var moisture = environmentData.SampleLocalMoistureNoise(sampleX, sampleZ);
                var depth = waterSurfaceHeight - terrainHeight;
                if (depth < 1.2f && basinNoise < 0.62f && marshMask < 0.52f)
                {
                    continue;
                }

                if ((moisture < 0.36f && basinNoise < 0.7f) || outcropMask > 0.58f)
                {
                    continue;
                }

                waterMask[x, z] = true;
                waterHeights[x, z] = waterSurfaceHeight + 0.05f;
            }
        }

        var visited = new bool[ChunkResolution, ChunkResolution];
        for (var z = 0; z < ChunkResolution; z++)
        {
            for (var x = 0; x < ChunkResolution; x++)
            {
                if (!waterMask[x, z] || visited[x, z])
                {
                    continue;
                }

                var width = 1;
                while ((x + width) < ChunkResolution && waterMask[x + width, z] && !visited[x + width, z])
                {
                    width++;
                }

                var depth = 1;
                var canExtend = true;
                while ((z + depth) < ChunkResolution && canExtend)
                {
                    for (var testX = 0; testX < width; testX++)
                    {
                        if (!waterMask[x + testX, z + depth] || visited[x + testX, z + depth])
                        {
                            canExtend = false;
                            break;
                        }
                    }

                    if (canExtend)
                    {
                        depth++;
                    }
                }

                for (var markZ = 0; markZ < depth; markZ++)
                {
                    for (var markX = 0; markX < width; markX++)
                    {
                        visited[x + markX, z + markZ] = true;
                    }
                }

                var surfaceY = waterHeights[x, z];
                var cellMinX = chunkMinX + x;
                var cellMaxX = cellMinX + width;
                var cellMinZ = chunkMinZ + z;
                var cellMaxZ = cellMinZ + depth;

                AppendQuad(
                    vertices,
                    indices,
                    new Float3(cellMinX, surfaceY, cellMinZ),
                    new Float3(cellMinX, surfaceY, cellMaxZ),
                    new Float3(cellMaxX, surfaceY, cellMaxZ),
                    new Float3(cellMaxX, surfaceY, cellMinZ),
                    new Float3(0f, 1f, 0f),
                    7);
            }
        }

        return vertices.Count == 0 ? null : new MeshBuildData(vertices.ToArray(), indices.ToArray());
    }

    private static void AppendBox(List<VoxelVertex> vertices, List<int> indices, Float3 center, Float3 halfExtents, ushort materialId)
    {
        var corners = new[]
        {
            new Float3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z)
        };

        AppendQuad(vertices, indices, corners[0], corners[1], corners[2], corners[3], new Float3(0f, 0f, -1f), materialId);
        AppendQuad(vertices, indices, corners[5], corners[4], corners[7], corners[6], new Float3(0f, 0f, 1f), materialId);
        AppendQuad(vertices, indices, corners[4], corners[0], corners[3], corners[7], new Float3(-1f, 0f, 0f), materialId);
        AppendQuad(vertices, indices, corners[1], corners[5], corners[6], corners[2], new Float3(1f, 0f, 0f), materialId);
        AppendQuad(vertices, indices, corners[3], corners[2], corners[6], corners[7], new Float3(0f, 1f, 0f), materialId);
        AppendQuad(vertices, indices, corners[4], corners[5], corners[1], corners[0], new Float3(0f, -1f, 0f), materialId);
    }

    private static void AppendQuad(List<VoxelVertex> vertices, List<int> indices, Float3 a, Float3 b, Float3 c, Float3 d, Float3 normal, ushort materialId)
    {
        var baseIndex = vertices.Count;
        vertices.Add(new VoxelVertex(a, normal, materialId));
        vertices.Add(new VoxelVertex(b, normal, materialId));
        vertices.Add(new VoxelVertex(c, normal, materialId));
        vertices.Add(new VoxelVertex(d, normal, materialId));
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }

    private static float Deterministic01(int x, int y, int z, int salt)
    {
        unchecked
        {
            var hash = salt;
            hash = (hash * 397) ^ x;
            hash = (hash * 397) ^ y;
            hash = (hash * 397) ^ z;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private static float DeterministicSigned(int x, int y, int z, int salt)
        => (Deterministic01(x, y, z, salt) * 2f) - 1f;

    private static float SampleBiomeNoise(float x, float z, int salt)
    {
        var x0 = FastFloor(x);
        var z0 = FastFloor(z);
        var tx = x - x0;
        var tz = z - z0;
        var sx = Smooth(tx);
        var sz = Smooth(tz);

        var v00 = Deterministic01(x0, salt, z0, salt + 13);
        var v10 = Deterministic01(x0 + 1, salt, z0, salt + 13);
        var v01 = Deterministic01(x0, salt, z0 + 1, salt + 13);
        var v11 = Deterministic01(x0 + 1, salt, z0 + 1, salt + 13);

        var ix0 = Mathf.Lerp(v00, v10, sx);
        var ix1 = Mathf.Lerp(v01, v11, sx);
        return Mathf.Lerp(ix0, ix1, sz);
    }

    private static float Smooth(float value)
        => value * value * (3f - (2f * value));

    private static int FastFloor(float value)
    {
        var truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }

    private readonly struct ChunkEnvironmentData
    {
        private readonly float _originX;
        private readonly float _originZ;
        private readonly int _sampleCount;
        private readonly float[,] _terrainHeight;
        private readonly float[,] _waterHeight;
        private readonly bool[,] _waterValid;
        private readonly float[,] _riverMask;
        private readonly float[,] _cliffMask;
        private readonly float[,] _outcropMask;
        private readonly float[,] _highlandMask;
        private readonly float[,] _erosionMask;
        private readonly float[,] _marshMask;
        private readonly float[,] _moisture;
        private readonly float[,] _temperature;
        private readonly float[,] _forestDensity;
        private readonly float[,] _rockyMask;
        private readonly float[,] _basinNoise;
        private readonly float[,] _localMoistureNoise;

        public ChunkEnvironmentData(
            float originX,
            float originZ,
            int sampleCount,
            float[,] terrainHeight,
            float[,] waterHeight,
            bool[,] waterValid,
            float[,] riverMask,
            float[,] cliffMask,
            float[,] outcropMask,
            float[,] highlandMask,
            float[,] erosionMask,
            float[,] marshMask,
            float[,] moisture,
            float[,] temperature,
            float[,] forestDensity,
            float[,] rockyMask,
            float[,] basinNoise,
            float[,] localMoistureNoise)
        {
            _originX = originX;
            _originZ = originZ;
            _sampleCount = sampleCount;
            _terrainHeight = terrainHeight;
            _waterHeight = waterHeight;
            _waterValid = waterValid;
            _riverMask = riverMask;
            _cliffMask = cliffMask;
            _outcropMask = outcropMask;
            _highlandMask = highlandMask;
            _erosionMask = erosionMask;
            _marshMask = marshMask;
            _moisture = moisture;
            _temperature = temperature;
            _forestDensity = forestDensity;
            _rockyMask = rockyMask;
            _basinNoise = basinNoise;
            _localMoistureNoise = localMoistureNoise;
        }

        public float SampleTerrainHeight(float x, float z)
            => SampleInterpolated(_terrainHeight, x, z);

        public float SampleWaterSurfaceHeight(float x, float z)
        {
            var xi = ClampIndex(Mathf.FloorToInt(x - _originX));
            var zi = ClampIndex(Mathf.FloorToInt(z - _originZ));
            return _waterValid[xi, zi] ? _waterHeight[xi, zi] : float.NegativeInfinity;
        }

        public float SampleRiverMask(float x, float z)
            => SampleInterpolated(_riverMask, x, z);

        public float SampleCliffMask(float x, float z)
            => SampleInterpolated(_cliffMask, x, z);

        public float SampleOutcropMask(float x, float z)
            => SampleInterpolated(_outcropMask, x, z);

        public float SampleHighlandMask(float x, float z)
            => SampleInterpolated(_highlandMask, x, z);

        public float SampleErosionMask(float x, float z)
            => SampleInterpolated(_erosionMask, x, z);

        public float SampleMarshMask(float x, float z)
            => SampleInterpolated(_marshMask, x, z);

        public float SampleMoisture(float x, float z)
            => SampleInterpolated(_moisture, x, z);

        public float SampleTemperature(float x, float z)
            => SampleInterpolated(_temperature, x, z);

        public float SampleForestDensity(float x, float z)
            => SampleInterpolated(_forestDensity, x, z);

        public float SampleRockyMask(float x, float z)
            => SampleInterpolated(_rockyMask, x, z);

        public float SampleBasinNoise(float x, float z)
            => SampleInterpolated(_basinNoise, x, z);

        public float SampleLocalMoistureNoise(float x, float z)
            => SampleInterpolated(_localMoistureNoise, x, z);

        private float SampleInterpolated(float[,] values, float x, float z)
        {
            var localX = Mathf.Clamp(x - _originX, 0f, _sampleCount - 1.001f);
            var localZ = Mathf.Clamp(z - _originZ, 0f, _sampleCount - 1.001f);
            var minX = ClampIndex(Mathf.FloorToInt(localX));
            var minZ = ClampIndex(Mathf.FloorToInt(localZ));
            var maxX = ClampIndex(minX + 1);
            var maxZ = ClampIndex(minZ + 1);
            var tx = localX - minX;
            var tz = localZ - minZ;

            var v00 = values[minX, minZ];
            var v10 = values[maxX, minZ];
            var v01 = values[minX, maxZ];
            var v11 = values[maxX, maxZ];
            var ix0 = Mathf.Lerp(v00, v10, tx);
            var ix1 = Mathf.Lerp(v01, v11, tx);
            return Mathf.Lerp(ix0, ix1, tz);
        }

        private int ClampIndex(int index)
        {
            if (index < 0)
            {
                return 0;
            }

            return index >= _sampleCount ? _sampleCount - 1 : index;
        }
    }

    private sealed class PooledChunkVisual
    {
        public PooledChunkVisual(
            GameObject root,
            MeshFilter terrainFilter,
            MeshRenderer terrainRenderer,
            GameObject treeObject,
            MeshFilter treeFilter,
            MeshRenderer treeRenderer,
            GameObject waterObject,
            MeshFilter waterFilter,
            MeshRenderer waterRenderer)
        {
            Root = root;
            TerrainFilter = terrainFilter;
            TerrainRenderer = terrainRenderer;
            TreeObject = treeObject;
            TreeFilter = treeFilter;
            TreeRenderer = treeRenderer;
            WaterObject = waterObject;
            WaterFilter = waterFilter;
            WaterRenderer = waterRenderer;
        }

        public GameObject Root { get; }

        public MeshFilter TerrainFilter { get; }

        public MeshRenderer TerrainRenderer { get; }

        public Mesh? TerrainMesh { get; set; }

        public MeshCollider? TerrainCollider { get; set; }

        public GameObject TreeObject { get; }

        public MeshFilter TreeFilter { get; }

        public MeshRenderer TreeRenderer { get; }

        public Mesh? TreeMesh { get; set; }

        public GameObject WaterObject { get; }

        public MeshFilter WaterFilter { get; }

        public MeshRenderer WaterRenderer { get; }

        public Mesh? WaterMesh { get; set; }
    }

    private readonly struct MeshBuildData
    {
        public MeshBuildData(VoxelVertex[] vertices, int[] indices)
        {
            Vertices = vertices;
            Indices = indices;
        }

        public VoxelVertex[] Vertices { get; }

        public int[] Indices { get; }
    }

    private readonly struct ChunkRenderData
    {
        public ChunkRenderData(Int3 coordinate, MeshBuildData terrainMesh, MeshBuildData? treeMesh, MeshBuildData? waterMesh, bool hasDetailMeshes)
        {
            Coordinate = coordinate;
            TerrainMesh = terrainMesh;
            TreeMesh = treeMesh;
            WaterMesh = waterMesh;
            HasDetailMeshes = hasDetailMeshes;
        }

        public Int3 Coordinate { get; }

        public MeshBuildData TerrainMesh { get; }

        public MeshBuildData? TreeMesh { get; }

        public MeshBuildData? WaterMesh { get; }

        public bool HasDetailMeshes { get; }
    }

    private readonly struct ChunkJobHandle
    {
        public ChunkJobHandle(Int3[] coordinates, int version, Task<IReadOnlyList<ChunkJobResult>> task)
        {
            Coordinates = coordinates;
            Version = version;
            Task = task;
        }

        public Int3[] Coordinates { get; }

        public int Version { get; }

        public Task<IReadOnlyList<ChunkJobResult>> Task { get; }
    }

    private readonly struct ChunkJobResult
    {
        public ChunkJobResult(Int3 coordinate, int version, ChunkRenderData? renderData)
        {
            Coordinate = coordinate;
            Version = version;
            RenderData = renderData;
        }

        public Int3 Coordinate { get; }

        public int Version { get; }

        public ChunkRenderData? RenderData { get; }
    }
}
