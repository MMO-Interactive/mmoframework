using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGameEngine.Engine;

public abstract class MonoGameEngineHost : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly SceneManager _sceneManager = new();

    private SpriteBatch? _spriteBatch;
    private readonly InputState _input = new();

    protected MonoGameEngineHost(string contentRoot = "Content")
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = contentRoot;
        IsMouseVisible = true;

        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 720;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        var context = BuildContext(deltaSeconds: 0f);
        _sceneManager.SetScene(CreateStartupScene(), context);
    }

    protected abstract Scene CreateStartupScene();

    protected override void Update(GameTime gameTime)
    {
        _input.Update();
        var context = BuildContext((float)gameTime.ElapsedGameTime.TotalSeconds);

        if (_input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            Exit();
            return;
        }

        _sceneManager.Update(context);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var context = BuildContext((float)gameTime.ElapsedGameTime.TotalSeconds);
        GraphicsDevice.Clear(new Color(13, 17, 24));

        _sceneManager.Draw(context);
        base.Draw(gameTime);
    }

    private EngineContext BuildContext(float deltaSeconds)
    {
        if (_spriteBatch is null)
        {
            throw new InvalidOperationException("SpriteBatch has not been initialized yet.");
        }

        return new EngineContext
        {
            GraphicsDevice = GraphicsDevice,
            Content = Content,
            SpriteBatch = _spriteBatch,
            Input = _input,
            DeltaSeconds = deltaSeconds
        };
    }
}
