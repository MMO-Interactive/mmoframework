using MonoGameEngine.Engine;

namespace MonoGameEngine.Demo;

public sealed class DemoGame : MonoGameEngineHost
{
    protected override Scene CreateStartupScene() => new GameplayScene();
}
