using MonoGameEngine.Editor;
using MonoGameEngine.Editor.Mmorpg;
using MonoGameEngine.Engine;
using MonoGameEngine.Terrain;

namespace MonoGameEngine.Demo;

public sealed class GameplayScene : Scene
{
    public GameplayScene()
    {
        var worldState = new EditorWorldState
        {
            Workspace = new MmorpgEditorWorkspace()
        };

        var assistant = new RuleBasedEditorAssistant();

        AddSystem(new TileTerrainRenderSystem(worldState));
        AddSystem(new PlayerMovementSystem(worldState));
        AddSystem(new AiFirstEditorSystem(worldState, assistant));
    }
}
