# MonoGame MMORPG Editor

Standalone AI-first MMORPG world editor built on `Client/MonoGameEngine`.

This project is separate from the reusable MonoGame engine scaffold. It focuses only on MMORPG development workflows:

- zone authoring
- NPC spawn placement
- resource spawn placement
- terrain regeneration and height scaling
- voxel structure box placement
- workspace save/load

## Run

```bash
dotnet run --project Client/MonoGameMmorpgEditor/MonoGameMmorpgEditor.csproj
```

## Generative AI Configuration

### ChatGPT subscription-backed Codex

The editor can call a local Codex SDK bridge, which is the route for using Codex with a ChatGPT subscription-backed login instead of API billing.

Prerequisites:

- Install Node.js.
- Sign in to Codex with ChatGPT through the Codex CLI, IDE extension, or Codex app.
- Install the bridge dependency once:

```powershell
cd Client/MonoGameMmorpgEditor/CodexBridge
npm install
```

Then run the editor with:

```powershell
dotnet run --project Client/MonoGameMmorpgEditor/MonoGameMmorpgEditor.csproj
```

Codex SDK is the default provider. Set `MMO_EDITOR_AI_PROVIDER` only if you want to force another provider.

Optional overrides:

```powershell
$env:MMO_EDITOR_CODEX_NODE="node"
$env:MMO_EDITOR_CODEX_SDK_BRIDGE="C:\absolute\path\to\codex-mmorpg-bridge.mjs"
```

This path depends on the local Codex SDK package and your Codex/ChatGPT login state. If the bridge fails, the editor falls back to the deterministic rule-based assistant.

### OpenAI API key path

The recommended OpenAI path uses the Responses API with a Codex model:

```powershell
$env:OPENAI_API_KEY="your_api_key"
$env:MMO_EDITOR_AI_PROVIDER="openai-codex"
$env:MMO_EDITOR_OPENAI_MODEL="gpt-5-codex"
$env:MMO_EDITOR_OPENAI_REASONING_EFFORT="medium"
```

Important: a ChatGPT subscription is not an API credential. This editor cannot directly spend or authenticate against a personal ChatGPT subscription from inside the MonoGame process. Use an OpenAI API key for in-editor AI calls, or use Codex separately through ChatGPT/Codex interfaces.

The editor also supports generic chat-completions-compatible providers when these variables are set:

```powershell
$env:MMO_EDITOR_AI_ENDPOINT="https://your-provider.example/v1/chat/completions"
$env:MMO_EDITOR_AI_API_KEY="your_api_key"
$env:MMO_EDITOR_AI_MODEL="your_model"
```

If any of those are missing, it falls back to the existing deterministic rule-based parser.

The endpoint is expected to be compatible with a chat-completions JSON shape.

## Prompt Examples

```text
create a starter zone called ember_valley
add three level 4 wolves near 120, 180
place an iron vein at 220, 144
build a small fort at 40, 40 to 52, 52 base 2 top 12 material 14
raise terrain height scale to 1.3
save workspace zones/ember_valley
```
