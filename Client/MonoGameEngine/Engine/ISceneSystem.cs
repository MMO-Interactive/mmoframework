namespace MonoGameEngine.Engine;

public interface ISceneSystem
{
    void Initialize(EngineContext context);
    void Update(EngineContext context);
    void Draw(EngineContext context);
}
