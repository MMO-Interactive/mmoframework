namespace MonoGameEngine.Engine;

public abstract class Scene
{
    private readonly List<ISceneSystem> _systems = new();

    protected void AddSystem(ISceneSystem system) => _systems.Add(system);

    public virtual void Initialize(EngineContext context)
    {
        foreach (var system in _systems)
        {
            system.Initialize(context);
        }
    }

    public virtual void Update(EngineContext context)
    {
        foreach (var system in _systems)
        {
            system.Update(context);
        }
    }

    public virtual void Draw(EngineContext context)
    {
        foreach (var system in _systems)
        {
            system.Draw(context);
        }
    }
}
