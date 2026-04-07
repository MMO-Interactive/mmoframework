import { Codex } from "@openai/codex-sdk";

const input = (await readStdin()).replace(/^\uFEFF/, "");
const request = JSON.parse(input);

const prompt = `You are an AI-first editor assistant for an MMORPG world editor.
Return only strict JSON. Do not include markdown.

Allowed JSON format:
{
  "message": "Short user-facing explanation with MMORPG worldbuilding attitude.",
  "actions": [
    { "type": "set_zone", "zoneId": "starter_zone" },
    { "type": "add_npc_spawn", "npcArchetype": "wolf", "level": 5, "x": 120, "z": 80 },
    { "type": "add_resource_spawn", "resourceType": "iron_vein", "x": 220, "z": 144 },
    { "type": "place_structure_box", "x1": 20, "z1": 20, "x2": 32, "z2": 32, "baseY": 2, "topY": 10, "materialId": 14 },
    { "type": "set_terrain_height_scale", "heightScale": 1.4 },
    { "type": "regenerate_terrain" },
    { "type": "save_workspace", "relativePath": "zones/starter_zone" },
    { "type": "load_workspace", "relativePath": "zones/starter_zone" }
  ]
}

Only create actions relevant to MMORPG development: zones, spawns, terrain, voxel structures, save/load.
Always include a short message, even when actions are applied.
Use flavorful MMORPG worldbuilding attitude, but keep it concise and practical.
If no editor action should be applied, return an empty actions array and put the useful answer in message.
Clamp NPC levels between 1 and 120.
Use X/Z coordinates for world positions.

Current editor context:
- Zone: ${request.context.activeZone.zoneId}
- Zone size: ${request.context.activeZone.widthTiles} x ${request.context.activeZone.heightTiles} tiles
- NPC spawns: ${request.context.npcSpawnCount}
- Resource spawns: ${request.context.resourceSpawnCount}
- Terrain height scale: ${request.context.terrainHeightScale}

User request:
${request.prompt}`;

const codex = new Codex();
const thread = codex.startThread({
  skipGitRepoCheck: true,
  sandboxMode: "read-only",
  approvalPolicy: "never",
  modelReasoningEffort: "low",
});
const result = await thread.run(prompt);
const text = extractText(result);

process.stdout.write(text);

function extractText(result) {
  if (typeof result === "string") {
    return result;
  }

  if (result?.finalResponse) {
    return result.finalResponse;
  }

  return JSON.stringify(result);
}

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }

  return Buffer.concat(chunks).toString("utf8");
}
