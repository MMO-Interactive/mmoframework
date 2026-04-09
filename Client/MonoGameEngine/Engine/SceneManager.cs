namespace MonoGameEngine.Engine;

public sealed class SceneManager
{
    private Scene? _activeScene;

    public void SetScene(Scene scene, EngineContext context)
    {
        _activeScene = scene;
        _activeScene.Initialize(context);
    }

    public void Update(EngineContext context) => _activeScene?.Update(context);

    public void Draw(EngineContext context) => _activeScene?.Draw(context);
}
