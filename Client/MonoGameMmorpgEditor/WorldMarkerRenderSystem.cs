using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameEngine.Editor;
using MonoGameEngine.Engine;
using MonoGameEngine.Terrain;

namespace MonoGameMmorpgEditor;

public sealed class WorldMarkerRenderSystem : ISceneSystem
{
    private readonly EditorWorldState _worldState;
    private readonly TileTerrainCamera _camera;

    private BasicEffect? _effect;

    public WorldMarkerRenderSystem(EditorWorldState worldState, TileTerrainCamera camera)
    {
        _worldState = worldState;
        _camera = camera;
    }

    public void Initialize(EngineContext context)
    {
        _effect = new BasicEffect(context.GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false
        };
    }

    public void Update(EngineContext context)
    {
    }

    public void Draw(EngineContext context)
    {
        if (_effect is null)
        {
            return;
        }

        var vertices = BuildMarkerVertices();
        if (vertices.Length == 0)
        {
            return;
        }

        var graphics = context.GraphicsDevice;
        var originalViewport = graphics.Viewport;
        var originalBlendState = graphics.BlendState;
        var originalDepthStencilState = graphics.DepthStencilState;
        var originalRasterizerState = graphics.RasterizerState;
        var viewportRectangle = MmorpgEditorShellSystem.GetTerrainViewport(originalViewport);
        graphics.Viewport = new Viewport(viewportRectangle);
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.BlendState = BlendState.Opaque;
        graphics.RasterizerState = RasterizerState.CullNone;

        _camera.BuildMatrices(graphics.Viewport);
        _effect.World = Matrix.Identity;
        _effect.View = _camera.View;
        _effect.Projection = _camera.Projection;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphics.DrawUserPrimitives(PrimitiveType.LineList, vertices, 0, vertices.Length / 2);
        }

        graphics.Viewport = originalViewport;
        graphics.BlendState = originalBlendState;
        graphics.DepthStencilState = originalDepthStencilState;
        graphics.RasterizerState = originalRasterizerState;
    }

    private VertexPositionColor[] BuildMarkerVertices()
    {
        var vertices = new List<VertexPositionColor>();

        foreach (var spawn in _worldState.Workspace.NpcSpawns)
        {
            AddMarker(vertices, spawn.Position.X, spawn.Position.Y, Color.IndianRed, height: 12f, radius: 3f);
        }

        foreach (var spawn in _worldState.Workspace.ResourceSpawns)
        {
            AddMarker(vertices, spawn.Position.X, spawn.Position.Y, Color.Goldenrod, height: 8f, radius: 2.5f);
        }

        return vertices.ToArray();
    }

    private void AddMarker(List<VertexPositionColor> vertices, float x, float z, Color color, float height, float radius)
    {
        var y = _worldState.Workspace.TerrainGenerator.GetTerrainHeight(x, z) * _worldState.TerrainHeightScale;
        var basePoint = new Vector3(x, y + 0.4f, z);
        var topPoint = new Vector3(x, y + height, z);

        AddLine(vertices, basePoint, topPoint, color);
        AddLine(vertices, topPoint + new Vector3(-radius, 0f, 0f), topPoint + new Vector3(radius, 0f, 0f), color);
        AddLine(vertices, topPoint + new Vector3(0f, 0f, -radius), topPoint + new Vector3(0f, 0f, radius), color);
        AddLine(vertices, topPoint + new Vector3(-radius, -radius, 0f), topPoint + new Vector3(radius, radius, 0f), color);
        AddLine(vertices, topPoint + new Vector3(radius, -radius, 0f), topPoint + new Vector3(-radius, radius, 0f), color);
    }

    private static void AddLine(List<VertexPositionColor> vertices, Vector3 start, Vector3 end, Color color)
    {
        vertices.Add(new VertexPositionColor(start, color));
        vertices.Add(new VertexPositionColor(end, color));
    }
}
