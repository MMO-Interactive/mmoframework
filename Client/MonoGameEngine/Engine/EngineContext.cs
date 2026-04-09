using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGameEngine.Engine;

public sealed class EngineContext
{
    public required GraphicsDevice GraphicsDevice { get; init; }
    public required ContentManager Content { get; init; }
    public required SpriteBatch SpriteBatch { get; init; }
    public required InputState Input { get; init; }
    public float DeltaSeconds { get; set; }
}
