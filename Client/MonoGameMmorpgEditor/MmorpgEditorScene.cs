using MonoGameEngine.Editor;
using MonoGameEngine.Editor.Mmorpg;
using MonoGameEngine.Engine;
using MonoGameEngine.Terrain;
using MonoGameMmorpgEditor.Ai;

namespace MonoGameMmorpgEditor;

public sealed class MmorpgEditorScene : Scene
{
    public MmorpgEditorScene()
    {
        var worldState = new EditorWorldState
        {
            Workspace = new MmorpgEditorWorkspace()
        };

        var fallbackAssistant = new RuleBasedEditorAssistant();
        var generativeAssistant = GenerativeMmorpgAssistantFactory.Create(fallbackAssistant);
        var terrainCamera = new TileTerrainCamera();

        AddSystem(new MmorpgEditorShellSystem());
        AddSystem(new TileTerrainRenderSystem(worldState, MmorpgEditorShellSystem.GetTerrainViewport, terrainCamera));
        AddSystem(new WorldMarkerRenderSystem(worldState, terrainCamera));
        AddSystem(new GenerativeMmorpgEditorSystem(worldState, generativeAssistant));
    }
}
