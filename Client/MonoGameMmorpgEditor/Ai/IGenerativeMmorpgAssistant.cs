using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public interface IGenerativeMmorpgAssistant
{
    string DisplayName { get; }

    Task<AssistantInterpretation> InterpretAsync(string prompt, MmorpgEditorContext context, CancellationToken cancellationToken);
}

public sealed record AssistantInterpretation(IReadOnlyList<EditorAction> Actions, string? Text = null)
{
    public static AssistantInterpretation FromActions(IReadOnlyList<EditorAction> actions)
        => new(actions);
}
