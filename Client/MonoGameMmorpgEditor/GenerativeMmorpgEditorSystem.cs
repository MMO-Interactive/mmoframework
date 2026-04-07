using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameEngine.Editor;
using MonoGameEngine.Editor.Mmorpg;
using MonoGameEngine.Engine;
using MonoGameMmorpgEditor.Ai;
using MonoGameMmorpgEditor.Ui;

namespace MonoGameMmorpgEditor;

public sealed class GenerativeMmorpgEditorSystem : ISceneSystem
{
    private const int AutoApplyActionLimit = 24;
    private const int HardActionLimit = 96;

    private readonly EditorWorldState _worldState;
    private readonly IGenerativeMmorpgAssistant _assistant;
    private readonly List<string> _recentPrompts = new();
    private readonly List<string> _statusLines = new();
    private readonly Stack<WorkspaceUndoSnapshot> _undoStack = new();

    private Texture2D? _pixel;
    private PixelTextRenderer? _text;
    private string _prompt = string.Empty;
    private Task<AssistantInterpretation>? _pendingRequest;
    private CancellationTokenSource? _pendingCancellation;
    private AssistantInterpretation? _pendingPreview;

    public GenerativeMmorpgEditorSystem(EditorWorldState worldState, IGenerativeMmorpgAssistant assistant)
    {
        _worldState = worldState;
        _assistant = assistant;
    }

    public void Initialize(EngineContext context)
    {
        _pixel = new Texture2D(context.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _text = new PixelTextRenderer(context.GraphicsDevice);
        _statusLines.Add($"Assistant: {_assistant.DisplayName}");
    }

    public void Update(EngineContext context)
    {
        CompletePendingRequest();

        foreach (var key in context.Input.PressedKeysThisFrame())
        {
            if (TryHandleControlKey(key))
            {
                continue;
            }

            if (_pendingRequest == null
                && TryMapKey(key, shift: context.Input.IsDown(Keys.LeftShift) || context.Input.IsDown(Keys.RightShift), out var c))
            {
                _prompt += c;
            }
        }
    }

    public void Draw(EngineContext context)
    {
        if (_pixel is null || _text is null)
        {
            return;
        }

        var viewport = context.GraphicsDevice.Viewport;
        var terrainViewport = MmorpgEditorShellSystem.GetTerrainViewport(viewport);
        var rightPanelX = viewport.Width - 318;
        var rightPanelTextWidth = 284;
        var promptY = viewport.Height - 78;
        var rightPanelClip = new Rectangle(rightPanelX, 136, 296, Math.Max(64, viewport.Height - 160));
        var promptClip = new Rectangle(24, promptY - 8, terrainViewport.Width - 16, 70);

        var originalScissor = context.GraphicsDevice.ScissorRectangle;
        context.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);

        _text.DrawText(context.SpriteBatch, "MMORPG GENERATIVE AI EDITOR", 32, 32, Color.White, scale: 2, maxWidth: viewport.Width - 96);
        _text.DrawText(context.SpriteBatch, $"ASSISTANT: {_assistant.DisplayName}", 32, 58, Color.LightCyan, scale: 1, maxWidth: viewport.Width - 96);
        _text.DrawText(context.SpriteBatch, "FOCUS: ZONES / SPAWNS / RESOURCES / TERRAIN / VOXEL STRUCTURES", 32, 72, Color.Gray, scale: 1, maxWidth: viewport.Width - 96);

        context.SpriteBatch.End();

        BeginClippedUi(context, promptClip);

        var promptWidth = Math.Clamp(_prompt.Length * 9, 12, Math.Max(12, promptClip.Width - 24));
        context.SpriteBatch.Draw(_pixel, new Rectangle(32, promptY + 20, promptWidth, 4), _pendingRequest == null ? Color.Cyan : Color.Yellow);
        _text.DrawText(context.SpriteBatch, _pendingRequest == null ? "PROMPT READY" : "AI THINKING", 32, promptY, _pendingRequest == null ? Color.Cyan : Color.Yellow, scale: 1, maxWidth: 120);
        _text.DrawWrappedText(context.SpriteBatch, _prompt, 168, promptY, Math.Max(16, promptClip.Right - 176), 18, Color.LightCyan, scale: 1);
        var promptHelp = _pendingPreview == null
            ? "ENTER SUBMITS / ESC CANCELS REQUEST / ARROWS ORBIT / Q E ZOOM / R REGEN"
            : "F5 APPLIES AI BATCH / F6 DISCARDS / F9 UNDOES LAST APPLIED PROMPT";
        _text.DrawWrappedText(context.SpriteBatch, promptHelp, 32, promptY + 40, Math.Max(16, promptClip.Width - 16), 22, Color.Gray, scale: 1);

        context.SpriteBatch.End();

        BeginClippedUi(context, rightPanelClip);

        var zoneWidth = Math.Clamp(_worldState.Workspace.ActiveZone.ZoneId.Length * 12, 40, viewport.Width / 2);
        _text.DrawText(context.SpriteBatch, "WORLD STATE", rightPanelX, 146, Color.White, scale: 2, maxWidth: rightPanelTextWidth);
        _text.DrawText(context.SpriteBatch, $"ZONE: {_worldState.Workspace.ActiveZone.ZoneId}", rightPanelX, 178, Color.DeepSkyBlue, scale: 1, maxWidth: rightPanelTextWidth);
        context.SpriteBatch.Draw(_pixel, new Rectangle(rightPanelX, 192, Math.Min(zoneWidth, 260), 5), Color.DeepSkyBlue);

        var npcWidth = Math.Clamp(_worldState.Workspace.NpcSpawns.Count * 16, 8, viewport.Width / 2);
        _text.DrawText(context.SpriteBatch, $"NPC SPAWNS: {_worldState.Workspace.NpcSpawns.Count}", rightPanelX, 214, Color.IndianRed, scale: 1, maxWidth: rightPanelTextWidth);
        context.SpriteBatch.Draw(_pixel, new Rectangle(rightPanelX, 228, Math.Min(npcWidth, 260), 5), Color.IndianRed);

        var resourceWidth = Math.Clamp(_worldState.Workspace.ResourceSpawns.Count * 16, 8, viewport.Width / 2);
        _text.DrawText(context.SpriteBatch, $"RESOURCES: {_worldState.Workspace.ResourceSpawns.Count}", rightPanelX, 250, Color.Goldenrod, scale: 1, maxWidth: rightPanelTextWidth);
        context.SpriteBatch.Draw(_pixel, new Rectangle(rightPanelX, 264, Math.Min(resourceWidth, 260), 5), Color.Goldenrod);

        var terrainWidth = (int)Math.Clamp(_worldState.TerrainHeightScale * 80f, 20f, viewport.Width / 2f);
        _text.DrawText(context.SpriteBatch, $"TERRAIN SCALE: {_worldState.TerrainHeightScale:0.00}", rightPanelX, 286, Color.SandyBrown, scale: 1, maxWidth: rightPanelTextWidth);
        context.SpriteBatch.Draw(_pixel, new Rectangle(rightPanelX, 300, Math.Min(terrainWidth, 260), 5), Color.SandyBrown);

        _text.DrawText(context.SpriteBatch, "TERRAIN VIEWPORT", terrainViewport.X + 12, terrainViewport.Y + 12, Color.White, scale: 1, maxWidth: terrainViewport.Width - 24);

        for (var i = 0; i < Math.Min(_recentPrompts.Count, 12); i++)
        {
            context.SpriteBatch.Draw(_pixel, new Rectangle(viewport.Width - 56 - (i * 14), 32, 9, 9), Color.MediumPurple);
        }

        _text.DrawText(context.SpriteBatch, "STATUS", rightPanelX, 334, Color.White, scale: 2, maxWidth: rightPanelTextWidth);
        var statusY = 366;
        for (var i = 0; i < Math.Min(_statusLines.Count, 4); i++)
        {
            statusY = _text.DrawWrappedText(
                context.SpriteBatch,
                _statusLines[i],
                rightPanelX,
                statusY,
                rightPanelTextWidth,
                Math.Max(0, 446 - statusY),
                i == 0 ? Color.LimeGreen : Color.Gray,
                scale: 1,
                lineSpacing: 3) + 5;
        }

        DrawCommandHelp(context, rightPanelX, 456);
        DrawMiniMap(context, rightPanelX, 600);

        context.SpriteBatch.End();
        context.GraphicsDevice.ScissorRectangle = originalScissor;
    }

    private bool TryHandleControlKey(Keys key)
    {
        if (key == Keys.F5 && _pendingRequest == null && _pendingPreview != null)
        {
            ApplyPendingPreview();
            return true;
        }

        if (key == Keys.F6 && _pendingRequest == null && _pendingPreview != null)
        {
            _pendingPreview = null;
            _statusLines.Insert(0, "Discarded pending AI batch");
            TrimStatusLines();
            return true;
        }

        if (key == Keys.F9 && _pendingRequest == null)
        {
            UndoLastChange();
            return true;
        }

        if (key == Keys.Back && _prompt.Length > 0 && _pendingRequest == null)
        {
            _prompt = _prompt[..^1];
            return true;
        }

        if (key == Keys.Enter && _pendingRequest == null)
        {
            SubmitPrompt();
            return true;
        }

        if (key == Keys.Escape && _pendingRequest != null)
        {
            _pendingCancellation?.Cancel();
            return true;
        }

        return false;
    }

    private void SubmitPrompt()
    {
        var prompt = _prompt.Trim();
        _prompt = string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _recentPrompts.Insert(0, prompt);
        if (_recentPrompts.Count > 24)
        {
            _recentPrompts.RemoveAt(_recentPrompts.Count - 1);
        }

        _statusLines.Insert(0, "Thinking...");
        TrimStatusLines();

        _pendingCancellation?.Dispose();
        _pendingCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _pendingRequest = _assistant.InterpretAsync(prompt, BuildContext(), _pendingCancellation.Token);
    }

    private void CompletePendingRequest()
    {
        if (_pendingRequest == null || !_pendingRequest.IsCompleted)
        {
            return;
        }

        try
        {
            var interpretation = _pendingRequest.GetAwaiter().GetResult();
            var actions = interpretation.Actions;
            if (actions.Count > AutoApplyActionLimit)
            {
                _pendingPreview = LimitPreview(interpretation);
                ShowPreview(_pendingPreview, actions.Count);
                return;
            }

            ApplyActions(actions);
            if (actions.Count == 0)
            {
                ShowAssistantTextOrFallback(interpretation.Text);
            }
            else
            {
                _statusLines.Insert(0, $"Applied {actions.Count} action(s)");
                if (!string.IsNullOrWhiteSpace(interpretation.Text))
                {
                    InsertAssistantText(interpretation.Text);
                }

                for (var i = Math.Min(actions.Count, 3) - 1; i >= 0; i--)
                {
                    _statusLines.Insert(1, DescribeAction(actions[i]));
                }
            }
        }
        catch (OperationCanceledException)
        {
            _statusLines.Insert(0, "Request canceled");
        }
        catch (Exception exception)
        {
            _statusLines.Insert(0, $"AI failed: {exception.GetType().Name}");
        }
        finally
        {
            _pendingRequest = null;
            _pendingCancellation?.Dispose();
            _pendingCancellation = null;
            TrimStatusLines();
        }
    }

    private MmorpgEditorContext BuildContext()
        => new(
            _worldState.Workspace.ActiveZone,
            _worldState.Workspace.NpcSpawns.Count,
            _worldState.Workspace.ResourceSpawns.Count,
            _worldState.TerrainHeightScale);

    private void ApplyActions(IReadOnlyList<EditorAction> actions)
    {
        if (actions.Count > 0)
        {
            _undoStack.Push(CaptureSnapshot());
            if (_undoStack.Count > 20)
            {
                var keptSnapshots = _undoStack.Take(20).Reverse().ToList();
                _undoStack.Clear();
                foreach (var snapshot in keptSnapshots)
                {
                    _undoStack.Push(snapshot);
                }
            }
        }

        foreach (var action in actions)
        {
            switch (action)
            {
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
                    _worldState.TerrainRegenerationRequested = true;
                    break;
            }
        }
    }

    private static AssistantInterpretation LimitPreview(AssistantInterpretation interpretation)
    {
        if (interpretation.Actions.Count <= HardActionLimit)
        {
            return interpretation;
        }

        return new AssistantInterpretation(
            interpretation.Actions.Take(HardActionLimit).ToList(),
            $"{interpretation.Text}\nBatch trimmed to {HardActionLimit} actions for editor safety.");
    }

    private void ShowPreview(AssistantInterpretation preview, int originalActionCount)
    {
        _statusLines.Insert(0, $"AI batch pending: {preview.Actions.Count}/{originalActionCount} actions");
        _statusLines.Insert(1, "Press F5 to apply, F6 to discard");

        foreach (var line in SummarizeActions(preview.Actions).Reverse())
        {
            _statusLines.Insert(2, line);
        }

        if (!string.IsNullOrWhiteSpace(preview.Text))
        {
            InsertAssistantText(preview.Text);
        }

        TrimStatusLines();
    }

    private void ApplyPendingPreview()
    {
        if (_pendingPreview == null)
        {
            return;
        }

        var preview = _pendingPreview;
        _pendingPreview = null;

        ApplyActions(preview.Actions);
        _statusLines.Insert(0, $"Applied pending AI batch: {preview.Actions.Count} action(s)");
        if (!string.IsNullOrWhiteSpace(preview.Text))
        {
            InsertAssistantText(preview.Text);
        }

        TrimStatusLines();
    }

    private static IReadOnlyList<string> SummarizeActions(IReadOnlyList<EditorAction> actions)
    {
        var lines = new List<string>();
        var npcCount = actions.OfType<AddNpcSpawnAction>().Count();
        var resourceCount = actions.OfType<AddResourceSpawnAction>().Count();
        var structureCount = actions.OfType<PlaceStructureBoxAction>().Count();
        var terrainCount = actions.Count(action => action is SetTerrainHeightScaleAction or RegenerateTerrainAction);
        var zoneCount = actions.OfType<SetZoneAction>().Count();

        if (npcCount > 0) lines.Add($"NPC spawns: {npcCount}");
        if (resourceCount > 0) lines.Add($"Resource spawns: {resourceCount}");
        if (structureCount > 0) lines.Add($"Structures: {structureCount}");
        if (terrainCount > 0) lines.Add($"Terrain edits: {terrainCount}");
        if (zoneCount > 0) lines.Add($"Zone edits: {zoneCount}");
        if (lines.Count == 0) lines.Add($"Other actions: {actions.Count}");

        return lines;
    }

    private void ShowAssistantTextOrFallback(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _statusLines.Insert(0, "No valid MMORPG actions parsed");
            return;
        }

        InsertAssistantText(text);
    }

    private void InsertAssistantText(string text)
    {
        foreach (var line in WrapText(text.Trim(), 56).Reverse())
        {
            _statusLines.Insert(0, line);
        }
    }

    private WorkspaceUndoSnapshot CaptureSnapshot()
        => new(
            _worldState.Workspace.Seed,
            _worldState.Workspace.ActiveZone,
            _worldState.Workspace.NpcSpawns.ToList(),
            _worldState.Workspace.ResourceSpawns.ToList(),
            _worldState.Workspace.StructureBoxes.ToList(),
            _worldState.TerrainHeightScale);

    private void RestoreSnapshot(WorkspaceUndoSnapshot snapshot)
    {
        _worldState.Workspace.RestoreState(
            snapshot.Seed,
            snapshot.ActiveZone,
            snapshot.NpcSpawns,
            snapshot.ResourceSpawns,
            snapshot.StructureBoxes);
        _worldState.TerrainHeightScale = snapshot.TerrainHeightScale;
        _worldState.TerrainRegenerationRequested = true;
    }

    private void UndoLastChange()
    {
        if (_undoStack.Count == 0)
        {
            _statusLines.Insert(0, "Nothing to undo");
            TrimStatusLines();
            return;
        }

        RestoreSnapshot(_undoStack.Pop());
        _statusLines.Insert(0, "Undid last applied prompt");
        TrimStatusLines();
    }

    private void TrimStatusLines()
    {
        while (_statusLines.Count > 8)
        {
            _statusLines.RemoveAt(_statusLines.Count - 1);
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

    private static string DescribeAction(EditorAction action)
        => action switch
        {
            SetTerrainHeightScaleAction setHeight => $"Terrain scale set to {setHeight.HeightScale:0.00}",
            RegenerateTerrainAction => "Terrain regenerated",
            SetZoneAction setZone => $"Zone set to {setZone.ZoneId}",
            AddNpcSpawnAction npc => $"NPC {npc.NpcArchetype} L{npc.Level} at {npc.Position.X:0}, {npc.Position.Y:0}",
            AddResourceSpawnAction resource => $"Resource {resource.ResourceType} at {resource.Position.X:0}, {resource.Position.Y:0}",
            PlaceStructureBoxAction structure => $"Structure box {structure.Min.X},{structure.Min.Y} to {structure.Max.X},{structure.Max.Y}",
            SaveWorkspaceAction save => $"Workspace saved: {save.RelativePath}",
            LoadWorkspaceAction load => $"Workspace loaded: {load.RelativePath}",
            _ => action.GetType().Name
        };

    private void DrawCommandHelp(EngineContext context, int x, int y)
    {
        if (_text is null)
        {
            return;
        }

        _text.DrawText(context.SpriteBatch, "COMMAND EXAMPLES", x, y, Color.White, scale: 2);
        var currentY = y + 32;
        currentY = _text.DrawWrappedText(context.SpriteBatch, "ADD NPC WOLF LEVEL 5 AT 120, 180", x, currentY, 284, 28, Color.Gray, scale: 1) + 4;
        currentY = _text.DrawWrappedText(context.SpriteBatch, "ADD RESOURCE IRON_VEIN AT 220, 144", x, currentY, 284, 28, Color.Gray, scale: 1) + 4;
        currentY = _text.DrawWrappedText(context.SpriteBatch, "BUILD BOX 40,40,52,52 BASE 2 TOP 12 MATERIAL 14", x, currentY, 284, 42, Color.Gray, scale: 1) + 4;
        currentY = _text.DrawWrappedText(context.SpriteBatch, "SAVE WORKSPACE ZONES/EMBER_VALLEY", x, currentY, 284, 28, Color.Gray, scale: 1) + 4;
        _text.DrawWrappedText(context.SpriteBatch, "F9 UNDOES LAST APPLIED PROMPT", x, currentY, 284, 28, Color.Gray, scale: 1);
    }

    private void DrawMiniMap(EngineContext context, int x, int y)
    {
        if (_pixel is null || _text is null)
        {
            return;
        }

        const int mapSize = 128;
        var zone = _worldState.Workspace.ActiveZone;
        var mapRect = new Rectangle(x, y + 24, mapSize, mapSize);
        context.SpriteBatch.Draw(_pixel, mapRect, new Color(12, 20, 18));
        _text.DrawText(context.SpriteBatch, "ZONE MAP", x, y, Color.White, scale: 2);

        foreach (var spawn in _worldState.Workspace.NpcSpawns)
        {
            DrawMapPoint(context, mapRect, zone.WidthTiles, zone.HeightTiles, spawn.Position.X, spawn.Position.Y, Color.IndianRed);
        }

        foreach (var spawn in _worldState.Workspace.ResourceSpawns)
        {
            DrawMapPoint(context, mapRect, zone.WidthTiles, zone.HeightTiles, spawn.Position.X, spawn.Position.Y, Color.Goldenrod);
        }
    }

    private void DrawMapPoint(EngineContext context, Rectangle mapRect, int zoneWidth, int zoneHeight, float x, float z, Color color)
    {
        if (_pixel is null)
        {
            return;
        }

        var normalizedX = Math.Clamp(x / Math.Max(zoneWidth, 1), 0f, 1f);
        var normalizedZ = Math.Clamp(z / Math.Max(zoneHeight, 1), 0f, 1f);
        var pointX = mapRect.X + (int)(normalizedX * (mapRect.Width - 1));
        var pointY = mapRect.Y + (int)(normalizedZ * (mapRect.Height - 1));
        context.SpriteBatch.Draw(_pixel, new Rectangle(pointX - 2, pointY - 2, 5, 5), color);
    }

    private static void BeginClippedUi(EngineContext context, Rectangle clip)
    {
        context.GraphicsDevice.ScissorRectangle = clip;
        context.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            new RasterizerState { ScissorTestEnable = true });
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";

    private static IEnumerable<string> WrapText(string value, int maxLineLength)
    {
        value = value.Replace("\r", string.Empty);
        foreach (var paragraph in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var remaining = paragraph;
            while (remaining.Length > maxLineLength)
            {
                var breakAt = remaining.LastIndexOf(' ', Math.Min(maxLineLength, remaining.Length - 1));
                if (breakAt <= 0)
                {
                    breakAt = maxLineLength;
                }

                yield return remaining[..breakAt].Trim();
                remaining = remaining[breakAt..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(remaining))
            {
                yield return remaining;
            }
        }
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

    private sealed record WorkspaceUndoSnapshot(
        int Seed,
        ZoneDefinition ActiveZone,
        List<NpcSpawnPoint> NpcSpawns,
        List<ResourceSpawnPoint> ResourceSpawns,
        List<StructureBoxPlacement> StructureBoxes,
        float TerrainHeightScale);
}
