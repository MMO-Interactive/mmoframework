using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

public sealed class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;

    private Vector2 _playerPosition = new(350f, 220f);
    private const float MoveSpeed = 220f;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _graphics.PreferredBackBufferWidth = 960;
        _graphics.PreferredBackBufferHeight = 540;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _pixel = new Texture2D(GraphicsDevice, width: 1, height: 1);
        _pixel.SetData(new[] { Color.White });
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape))
        {
            Exit();
            return;
        }

        var direction = Vector2.Zero;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up)) direction.Y -= 1f;
        if (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)) direction.Y += 1f;
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left)) direction.X -= 1f;
        if (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right)) direction.X += 1f;

        if (direction != Vector2.Zero)
        {
            direction.Normalize();
            var deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _playerPosition += direction * MoveSpeed * deltaSeconds;
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_spriteBatch is null || _pixel is null)
        {
            return;
        }

        GraphicsDevice.Clear(new Color(18, 23, 35));

        _spriteBatch.Begin();
        _spriteBatch.Draw(
            _pixel,
            destinationRectangle: new Rectangle((int)_playerPosition.X, (int)_playerPosition.Y, width: 64, height: 64),
            color: Color.CornflowerBlue);
        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
