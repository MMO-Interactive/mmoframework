using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameEngine.Engine;

namespace MonoGameEngine.Terrain;

public sealed class TileTerrainCamera
{
    private float _yaw = 0.8f;
    private float _pitch = 0.65f;
    private float _distance = 220f;
    private Vector3 _target = new(96f, 12f, 96f);

    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;

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
    }

    public void BuildMatrices(Viewport viewport)
    {
        var cameraOffset = new Vector3(
            MathF.Cos(_yaw) * MathF.Cos(_pitch),
            MathF.Sin(_pitch),
            MathF.Sin(_yaw) * MathF.Cos(_pitch)) * _distance;

        var cameraPosition = _target + cameraOffset;

        View = Matrix.CreateLookAt(cameraPosition, _target, Vector3.Up);
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4,
            viewport.AspectRatio,
            0.1f,
            2000f);
    }
}
