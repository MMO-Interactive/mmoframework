using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameEngine.Editor;
using MonoGameEngine.Engine;

namespace MonoGameEngine.Demo;

public sealed class PlayerMovementSystem : ISceneSystem
{
    private readonly EditorWorldState _worldState;

    private Texture2D? _pixel;
    private Vector2 _position = new(610f, 330f);

    public PlayerMovementSystem(EditorWorldState worldState)
    {
        _worldState = worldState;
    }

    public void Initialize(EngineContext context)
    {
        _pixel = new Texture2D(context.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void Update(EngineContext context)
    {
        var direction = Vector2.Zero;

        if (context.Input.IsDown(Keys.W) || context.Input.IsDown(Keys.Up)) direction.Y -= 1f;
        if (context.Input.IsDown(Keys.S) || context.Input.IsDown(Keys.Down)) direction.Y += 1f;
        if (context.Input.IsDown(Keys.A) || context.Input.IsDown(Keys.Left)) direction.X -= 1f;
        if (context.Input.IsDown(Keys.D) || context.Input.IsDown(Keys.Right)) direction.X += 1f;

        if (direction != Vector2.Zero)
        {
            direction.Normalize();
        }

        _position += direction * _worldState.PlayerSpeed * context.DeltaSeconds;

        var viewport = context.GraphicsDevice.Viewport;
        _position.X = Math.Clamp(_position.X, 0, viewport.Width - 64);
        _position.Y = Math.Clamp(_position.Y, 0, viewport.Height - 64);
    }

    public void Draw(EngineContext context)
    {
        if (_pixel is null)
        {
            return;
        }

        context.SpriteBatch.Begin();

        foreach (var marker in _worldState.Markers)
        {
            context.SpriteBatch.Draw(_pixel, new Rectangle((int)marker.X - 6, (int)marker.Y - 6, 12, 12), Color.Fuchsia);
        }

        foreach (var npcSpawn in _worldState.Workspace.NpcSpawns)
        {
            context.SpriteBatch.Draw(_pixel, new Rectangle((int)npcSpawn.Position.X - 4, (int)npcSpawn.Position.Y - 4, 8, 8), Color.Red);
        }

        foreach (var resourceSpawn in _worldState.Workspace.ResourceSpawns)
        {
            context.SpriteBatch.Draw(_pixel, new Rectangle((int)resourceSpawn.Position.X - 4, (int)resourceSpawn.Position.Y - 4, 8, 8), Color.Yellow);
        }

        context.SpriteBatch.Draw(_pixel, new Rectangle((int)_position.X, (int)_position.Y, 64, 64), _worldState.PlayerColor);

        context.SpriteBatch.End();
    }
}
