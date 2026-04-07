using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public sealed class RuleBasedMmorpgAssistantAdapter : IGenerativeMmorpgAssistant
{
    private readonly IEditorAssistant _assistant;

    public RuleBasedMmorpgAssistantAdapter(IEditorAssistant assistant)
    {
        _assistant = assistant;
    }

    public string DisplayName => "Rule-Based Fallback Assistant";

    public Task<AssistantInterpretation> InterpretAsync(
        string prompt,
        MmorpgEditorContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(AssistantInterpretation.FromActions(_assistant.Interpret(prompt)));
}
