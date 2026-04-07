using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameEngine.Editor;
using MonoGameEngine.Engine;

namespace MonoGameEngine.Terrain;

public sealed class TileTerrainRenderSystem : ISceneSystem
{
    private readonly EditorWorldState _worldState;

    private BasicEffect? _effect;
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;

    private int _primitiveCount;

    private float _yaw = 0.8f;
    private float _pitch = 0.65f;
    private float _distance = 220f;
    private Vector3 _target = new(96f, 12f, 96f);

    public TileTerrainRenderSystem(EditorWorldState worldState)
    {
        _worldState = worldState;
    }

    public void Initialize(EngineContext context)
    {
        _effect = new BasicEffect(context.GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false
        };

        RebuildTerrain(context.GraphicsDevice);
    }

    public void Update(EngineContext context)
    {
        var input = context.Input;

        if (input.IsDown(Keys.Left)) _yaw += 1.4f * context.DeltaSeconds;
        if (input.IsDown(Keys.Right)) _yaw -= 1.4f * context.DeltaSeconds;
        if (input.IsDown(Keys.Up)) _pitch += 1.0f * context.DeltaSeconds;
        if (input.IsDown(Keys.Down)) _pitch -= 1.0f * context.DeltaSeconds;
        if (input.IsDown(Keys.Q)) _distance += 80f * context.DeltaSeconds;
        if (input.IsDown(Keys.E)) _distance -= 80f * context.DeltaSeconds;

        _pitch = Math.Clamp(_pitch, 0.15f, 1.3f);
        _distance = Math.Clamp(_distance, 90f, 420f);

        if (_worldState.TerrainRegenerationRequested || input.IsPressed(Keys.R))
        {
            _worldState.TerrainRegenerationRequested = false;
            if (input.IsPressed(Keys.R))
            {
                _worldState.Workspace.RegenerateTerrain();
            }

            RebuildTerrain(context.GraphicsDevice);
        }
    }

    public void Draw(EngineContext context)
    {
        if (_effect is null || _vertexBuffer is null || _indexBuffer is null)
        {
            return;
        }

        var graphics = context.GraphicsDevice;
        graphics.BlendState = BlendState.Opaque;
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;

        var cameraOffset = new Vector3(
            MathF.Cos(_yaw) * MathF.Cos(_pitch),
            MathF.Sin(_pitch),
            MathF.Sin(_yaw) * MathF.Cos(_pitch)) * _distance;

        var cameraPosition = _target + cameraOffset;

        _effect.World = Matrix.Identity;
        _effect.View = Matrix.CreateLookAt(cameraPosition, _target, Vector3.Up);
        _effect.Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4,
            graphics.Viewport.AspectRatio,
            0.1f,
            2000f);

        graphics.SetVertexBuffer(_vertexBuffer);
        graphics.Indices = _indexBuffer;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphics.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    private void RebuildTerrain(GraphicsDevice graphicsDevice)
    {
        var terrain = new TileTerrain3D(
            tilesX: 48,
            tilesZ: 48,
            tileSize: 4f,
            heightSampler: _worldState.Workspace.TerrainGenerator.GetTerrainHeight,
            heightMultiplier: _worldState.TerrainHeightScale);

        var mesh = terrain.BuildMesh();

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();

        _vertexBuffer = new VertexBuffer(graphicsDevice, typeof(VertexPositionColor), mesh.Vertices.Length, BufferUsage.WriteOnly);
        _vertexBuffer.SetData(mesh.Vertices);

        _indexBuffer = new IndexBuffer(graphicsDevice, IndexElementSize.ThirtyTwoBits, mesh.Indices.Length, BufferUsage.WriteOnly);
        _indexBuffer.SetData(mesh.Indices);

        _primitiveCount = mesh.Indices.Length / 3;
    }
}
