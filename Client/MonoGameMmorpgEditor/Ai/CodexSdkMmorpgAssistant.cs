using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public sealed class CodexSdkMmorpgAssistant : IGenerativeMmorpgAssistant
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _nodeExecutable;
    private readonly string _bridgeScriptPath;
    private readonly IEditorAssistant _fallbackAssistant;

    public CodexSdkMmorpgAssistant(string nodeExecutable, string bridgeScriptPath, IEditorAssistant fallbackAssistant)
    {
        _nodeExecutable = nodeExecutable;
        _bridgeScriptPath = bridgeScriptPath;
        _fallbackAssistant = fallbackAssistant;
    }

    public string DisplayName => "Codex SDK Bridge";

    public async Task<AssistantInterpretation> InterpretAsync(
        string prompt,
        MmorpgEditorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestJson = JsonSerializer.Serialize(new CodexBridgeRequest(prompt, context), JsonOptions);
            using var process = StartBridgeProcess();

            await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                var error = await errorTask.ConfigureAwait(false);
                return new AssistantInterpretation(
                    _fallbackAssistant.Interpret(prompt),
                    string.IsNullOrWhiteSpace(error)
                        ? $"Codex SDK bridge exited with code {process.ExitCode} and no output."
                        : $"Codex SDK bridge failed: {error.Trim()}");
            }

            var actions = MmorpgEditorJsonActionParser.Parse(output);
            var message = MmorpgEditorJsonActionParser.TryParseMessage(output);
            return new AssistantInterpretation(actions, string.IsNullOrWhiteSpace(message) ? output.Trim() : message.Trim());
        }
        catch
        {
            return AssistantInterpretation.FromActions(_fallbackAssistant.Interpret(prompt));
        }
    }

    private Process StartBridgeProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _nodeExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add(_bridgeScriptPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Codex SDK bridge process.");
    }

    private sealed record CodexBridgeRequest(string Prompt, MmorpgEditorContext Context);
}
