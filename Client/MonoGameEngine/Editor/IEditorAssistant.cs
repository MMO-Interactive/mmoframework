namespace MonoGameEngine.Editor;

public interface IEditorAssistant
{
    IReadOnlyList<EditorAction> Interpret(string prompt);
}
