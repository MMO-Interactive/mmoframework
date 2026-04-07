using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameEngine.Engine;
using MonoGameEngine.Editor.Mmorpg;

namespace MonoGameEngine.Editor;

public sealed class AiFirstEditorSystem : ISceneSystem
{
    private readonly EditorWorldState _worldState;
    private readonly IEditorAssistant _assistant;

    private Texture2D? _pixel;
    private readonly List<string> _recentPrompts = new();

    private bool _editorOpen = true;
    private string _prompt = string.Empty;

    public AiFirstEditorSystem(EditorWorldState worldState, IEditorAssistant assistant)
    {
        _worldState = worldState;
        _assistant = assistant;
    }

    public void Initialize(EngineContext context)
    {
        _pixel = new Texture2D(context.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void Update(EngineContext context)
    {
        if (context.Input.IsPressed(Keys.Tab))
        {
            _editorOpen = !_editorOpen;
        }

        if (!_editorOpen)
        {
            return;
        }

        foreach (var key in context.Input.PressedKeysThisFrame())
        {
            if (TryHandleControlKey(key))
            {
                continue;
            }

            if (TryMapKey(key, shift: context.Input.IsDown(Keys.LeftShift) || context.Input.IsDown(Keys.RightShift), out var c))
            {
                _prompt += c;
            }
        }
    }

    public void Draw(EngineContext context)
    {
        if (_pixel is null || !_editorOpen)
        {
            return;
        }

        var viewport = context.GraphicsDevice.Viewport;
        context.SpriteBatch.Begin();

        // Editor background panel.
        context.SpriteBatch.Draw(_pixel, new Rectangle(16, 16, viewport.Width - 32, 138), new Color(8, 10, 15, 220));

        // Prompt length bar (represents current natural-language prompt input size).
        var width = Math.Clamp(_prompt.Length * 8, 0, viewport.Width - 64);
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, 40, width, 12), Color.Cyan);

        // Status chips: speed, marker count, terrain height scale, and MMO spawn counts.
        var speedWidth = (int)Math.Clamp(_worldState.PlayerSpeed, 40f, 800f) / 4;
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, 68, speedWidth, 10), Color.Orange);

        var markerWidth = Math.Clamp(_worldState.Markers.Count * 20, 0, viewport.Width / 2);
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, 88, markerWidth, 10), Color.LimeGreen);

        var terrainWidth = (int)Math.Clamp(_worldState.TerrainHeightScale * 64f, 16f, viewport.Width / 2f);
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, 106, terrainWidth, 10), Color.SandyBrown);

        var npcWidth = Math.Clamp(_worldState.Workspace.NpcSpawns.Count * 12, 0, viewport.Width / 2);
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, 124, npcWidth, 8), Color.IndianRed);

        var resourceWidth = Math.Clamp(_worldState.Workspace.ResourceSpawns.Count * 12, 0, viewport.Width / 2);
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, 136, resourceWidth, 8), Color.Goldenrod);

        // Recent prompt indicators.
        for (var i = 0; i < _recentPrompts.Count; i++)
        {
            context.SpriteBatch.Draw(_pixel, new Rectangle(viewport.Width - 48 - (i * 16), 28, 10, 10), Color.MediumPurple);
        }

        context.SpriteBatch.End();
    }

    private bool TryHandleControlKey(Keys key)
    {
        if (key == Keys.Back && _prompt.Length > 0)
        {
            _prompt = _prompt[..^1];
            return true;
        }

        if (key != Keys.Enter)
        {
            return false;
        }

        ApplyPrompt();
        return true;
    }

    private void ApplyPrompt()
    {
        var prompt = _prompt.Trim();
        _prompt = string.Empty;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _recentPrompts.Insert(0, prompt);
        if (_recentPrompts.Count > 10)
        {
            _recentPrompts.RemoveAt(_recentPrompts.Count - 1);
        }

        var actions = _assistant.Interpret(prompt);
        foreach (var action in actions)
        {
            switch (action)
            {
                case SetPlayerSpeedAction setSpeed:
                    _worldState.PlayerSpeed = setSpeed.Speed;
                    break;
                case SetPlayerColorAction setColor:
                    _worldState.PlayerColor = setColor.Color;
                    break;
                case SpawnMarkerAction spawn:
                    _worldState.Markers.Add(spawn.Position);
                    break;
                case ClearMarkersAction:
                    _worldState.Markers.Clear();
                    break;
                case SetTerrainHeightScaleAction setHeight:
                    _worldState.TerrainHeightScale = setHeight.HeightScale;
                    _worldState.TerrainRegenerationRequested = true;
                    break;
                case RegenerateTerrainAction:
                    _worldState.Workspace.RegenerateTerrain();
                    _worldState.TerrainRegenerationRequested = true;
                    break;
                case SetZoneAction setZone:
                    _worldState.Workspace.SetZone(setZone.ZoneId);
                    break;
                case AddNpcSpawnAction npcSpawn:
                    _worldState.Workspace.AddNpcSpawn(npcSpawn.NpcArchetype, npcSpawn.Level, npcSpawn.Position);
                    break;
                case AddResourceSpawnAction resourceSpawn:
                    _worldState.Workspace.AddResourceSpawn(resourceSpawn.ResourceType, resourceSpawn.Position);
                    break;
                case PlaceStructureBoxAction structureBox:
                    _worldState.Workspace.PlaceStructureBox(structureBox.Min, structureBox.Max, structureBox.BaseY, structureBox.TopY, structureBox.MaterialId);
                    _worldState.TerrainRegenerationRequested = true;
                    break;
                case SaveWorkspaceAction saveWorkspace:
                    MmorpgWorkspacePersistence.Save(_worldState.Workspace, ResolveWorkspacePath(saveWorkspace.RelativePath));
                    break;
                case LoadWorkspaceAction loadWorkspace:
                    MmorpgWorkspacePersistence.LoadInto(_worldState.Workspace, ResolveWorkspacePath(loadWorkspace.RelativePath));
                    break;
            }
        }
    }

    private static string ResolveWorkspacePath(string relativePath)
    {
        var workspaceRoot = Path.Combine(AppContext.BaseDirectory, "EditorData");
        var normalized = relativePath.Replace("..", string.Empty).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".json";
        }

        return Path.Combine(workspaceRoot, normalized);
    }

    private static bool TryMapKey(Keys key, bool shift, out char c)
    {
        c = '\0';

        if (key is >= Keys.A and <= Keys.Z)
        {
            var baseChar = (char)('a' + (key - Keys.A));
            c = shift ? char.ToUpperInvariant(baseChar) : baseChar;
            return true;
        }

        if (key is >= Keys.D0 and <= Keys.D9)
        {
            c = (char)('0' + (key - Keys.D0));
            return true;
        }

        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            c = (char)('0' + (key - Keys.NumPad0));
            return true;
        }

        c = key switch
        {
            Keys.Space => ' ',
            Keys.OemComma => ',',
            Keys.OemPeriod => '.',
            Keys.OemMinus => '-',
            Keys.OemPlus => shift ? '+' : '=',
            Keys.OemQuestion => shift ? '?' : '/',
            Keys.OemSemicolon => shift ? ':' : ';',
            Keys.OemQuotes => shift ? '"' : '\'',
            Keys.OemOpenBrackets => shift ? '{' : '[',
            Keys.OemCloseBrackets => shift ? '}' : ']',
            _ => '\0'
        };

        return c != '\0';
    }
}
