using Microsoft.Xna.Framework;
using MonoGameEngine.Editor.Mmorpg;

namespace MonoGameEngine.Editor;

public sealed class EditorWorldState
{
    public float PlayerSpeed { get; set; } = 260f;
    public Color PlayerColor { get; set; } = Color.Orange;
    public List<Vector2> Markers { get; } = new();

    public float TerrainHeightScale { get; set; } = 1f;
    public bool TerrainRegenerationRequested { get; set; }

    public required MmorpgEditorWorkspace Workspace { get; init; }
}
