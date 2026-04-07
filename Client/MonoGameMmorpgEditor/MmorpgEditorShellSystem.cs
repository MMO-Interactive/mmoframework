using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameEngine.Engine;

namespace MonoGameMmorpgEditor;

public sealed class MmorpgEditorShellSystem : ISceneSystem
{
    private Texture2D? _pixel;

    public void Initialize(EngineContext context)
    {
        _pixel = new Texture2D(context.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void Update(EngineContext context)
    {
    }

    public void Draw(EngineContext context)
    {
        if (_pixel is null)
        {
            return;
        }

        var viewport = context.GraphicsDevice.Viewport;
        var terrainViewport = GetTerrainViewport(viewport);
        var rightPanel = new Rectangle(viewport.Width - 336, 128, 320, viewport.Height - 144);
        var topPanel = new Rectangle(16, 16, viewport.Width - 32, 96);
        var promptPanel = new Rectangle(16, viewport.Height - 92, terrainViewport.Width, 76);

        context.GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(9, 12, 16), 1f, 0);

        context.SpriteBatch.Begin();
        context.SpriteBatch.Draw(_pixel, viewport.Bounds, new Color(9, 12, 16));
        context.SpriteBatch.Draw(_pixel, topPanel, new Color(4, 9, 14));
        context.SpriteBatch.Draw(_pixel, Expand(terrainViewport, 4), new Color(35, 44, 52));
        context.SpriteBatch.Draw(_pixel, terrainViewport, new Color(13, 17, 24));
        context.SpriteBatch.Draw(_pixel, rightPanel, new Color(5, 9, 13));
        context.SpriteBatch.Draw(_pixel, promptPanel, new Color(5, 9, 13));
        context.SpriteBatch.End();
    }

    public static Rectangle GetTerrainViewport(Viewport viewport)
    {
        var rightPanelWidth = 352;
        var top = 128;
        var bottom = 108;
        return new Rectangle(
            24,
            top,
            Math.Max(320, viewport.Width - rightPanelWidth - 40),
            Math.Max(240, viewport.Height - top - bottom));
    }

    private static Rectangle Expand(Rectangle rectangle, int amount)
        => new(
            rectangle.X - amount,
            rectangle.Y - amount,
            rectangle.Width + (amount * 2),
            rectangle.Height + (amount * 2));
}
