using MonoGameEngine.Editor;

namespace MonoGameMmorpgEditor.Ai;

public static class GenerativeMmorpgAssistantFactory
{
    public static IGenerativeMmorpgAssistant Create(IEditorAssistant fallbackAssistant)
    {
        var provider = Environment.GetEnvironmentVariable("MMO_EDITOR_AI_PROVIDER");
        if (string.IsNullOrWhiteSpace(provider)
            || string.Equals(provider, "codex-sdk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "chatgpt-codex", StringComparison.OrdinalIgnoreCase))
        {
            return CreateCodexSdkAssistant(fallbackAssistant);
        }

        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("MMO_EDITOR_OPENAI_API_KEY");

        var endpoint = Environment.GetEnvironmentVariable("MMO_EDITOR_AI_ENDPOINT");
        if (ShouldUseOpenAiCodex(provider, openAiApiKey, endpoint))
        {
            var openAiModel = Environment.GetEnvironmentVariable("MMO_EDITOR_OPENAI_MODEL")
                ?? Environment.GetEnvironmentVariable("MMO_EDITOR_AI_MODEL")
                ?? "gpt-5-codex";
            var reasoningEffort = Environment.GetEnvironmentVariable("MMO_EDITOR_OPENAI_REASONING_EFFORT") ?? "medium";
            var endpointText = Environment.GetEnvironmentVariable("MMO_EDITOR_OPENAI_RESPONSES_ENDPOINT");
            var openAiEndpoint = Uri.TryCreate(endpointText, UriKind.Absolute, out var parsedEndpoint) ? parsedEndpoint : null;

            return new OpenAiResponsesMmorpgAssistant(openAiApiKey!, openAiModel, reasoningEffort, fallbackAssistant, openAiEndpoint);
        }

        var apiKey = Environment.GetEnvironmentVariable("MMO_EDITOR_AI_API_KEY");
        var model = Environment.GetEnvironmentVariable("MMO_EDITOR_AI_MODEL");

        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(model))
        {
            return new RuleBasedMmorpgAssistantAdapter(fallbackAssistant);
        }

        return new OpenAiCompatibleMmorpgAssistant(new Uri(endpoint), apiKey, model, fallbackAssistant);
    }

    private static bool ShouldUseOpenAiCodex(string? provider, string? apiKey, string? genericEndpoint)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        return string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "openai-codex", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(genericEndpoint));
    }

    private static IGenerativeMmorpgAssistant CreateCodexSdkAssistant(IEditorAssistant fallbackAssistant)
    {
        var nodeExecutable = Environment.GetEnvironmentVariable("MMO_EDITOR_CODEX_NODE") ?? "node";
        var bridgeScriptPath = Environment.GetEnvironmentVariable("MMO_EDITOR_CODEX_SDK_BRIDGE")
            ?? ResolveBundledCodexBridgePath();

        return File.Exists(bridgeScriptPath)
            ? new CodexSdkMmorpgAssistant(nodeExecutable, bridgeScriptPath, fallbackAssistant)
            : new RuleBasedMmorpgAssistantAdapter(fallbackAssistant);
    }

    private static string ResolveBundledCodexBridgePath()
        => Path.Combine(AppContext.BaseDirectory, "CodexBridge", "codex-mmorpg-bridge.mjs");
}
