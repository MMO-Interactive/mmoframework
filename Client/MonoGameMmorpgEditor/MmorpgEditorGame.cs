using MonoGameEngine.Engine;

namespace MonoGameMmorpgEditor;

public sealed class MmorpgEditorGame : MonoGameEngineHost
{
    protected override Scene CreateStartupScene() => new MmorpgEditorScene();
}
