using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameEngine.Editor;
using MonoGameEngine.Engine;

namespace MonoGameEngine.Terrain;

public sealed class TileTerrainRenderSystem : ISceneSystem
{
    private readonly EditorWorldState _worldState;
    private readonly Func<Viewport, Rectangle>? _viewportFactory;
    private readonly TileTerrainCamera _camera;

    private BasicEffect? _effect;
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;

    private int _primitiveCount;

    public TileTerrainRenderSystem(
        EditorWorldState worldState,
        Func<Viewport, Rectangle>? viewportFactory = null,
        TileTerrainCamera? camera = null)
    {
        _worldState = worldState;
        _viewportFactory = viewportFactory;
        _camera = camera ?? new TileTerrainCamera();
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
        _camera.Update(context);

        if (_worldState.TerrainRegenerationRequested || context.Input.IsPressed(Keys.R))
        {
            _worldState.TerrainRegenerationRequested = false;
            if (context.Input.IsPressed(Keys.R))
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
        var originalViewport = graphics.Viewport;
        var originalBlendState = graphics.BlendState;
        var originalDepthStencilState = graphics.DepthStencilState;
        var originalRasterizerState = graphics.RasterizerState;
        var viewportRectangle = _viewportFactory?.Invoke(originalViewport) ?? originalViewport.Bounds;
        graphics.Viewport = new Viewport(viewportRectangle);
        graphics.BlendState = BlendState.Opaque;
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;
        graphics.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

        _camera.BuildMatrices(graphics.Viewport);
        _effect.World = Matrix.Identity;
        _effect.View = _camera.View;
        _effect.Projection = _camera.Projection;

        graphics.SetVertexBuffer(_vertexBuffer);
        graphics.Indices = _indexBuffer;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphics.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }

        graphics.Viewport = originalViewport;
        graphics.BlendState = originalBlendState;
        graphics.DepthStencilState = originalDepthStencilState;
        graphics.RasterizerState = originalRasterizerState;
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
