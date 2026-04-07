import { Codex } from "@openai/codex-sdk";

const input = (await readStdin()).replace(/^\uFEFF/, "");
console.error("CODEX_STATUS Bridge received Unity request.");
const request = JSON.parse(input);
const attachedImagePaths = Array.isArray(request.attachedImagePaths)
  ? request.attachedImagePaths.filter((path) => typeof path === "string" && path.length > 0)
  : [];

const prompt = `You are Codex controlling the Unity Editor for this MMORPG Unity client.
Return only strict JSON. Do not include markdown.
Return a short message, a safe visible plan summary, and a list of Unity editor actions to execute.

Allowed JSON format:
{
  "message": "Short direct status with MMORPG creation flavor.",
  "plan": [
    "Interpret the request as a starter-zone HUD pass.",
    "Create a Canvas and stable parent hierarchy.",
    "Add top bar, quest panel, and action button UI.",
    "Save the scene after applying generated objects."
  ],
  "actions": [
    { "type": "create_game_object", "name": "Wolf Spawn Marker", "primitive": "Cube", "x": 0, "y": 1, "z": 0 },
    { "type": "rename_object", "name": "Wolf Spawn Marker", "newName": "Starter Wolf Spawn Marker" },
    { "type": "set_active", "name": "Starter Wolf Spawn Marker", "active": true },
    { "type": "set_parent", "name": "Starter Wolf Spawn Marker", "parent": "Encounter Markers" },
    { "type": "set_transform", "name": "Starter Wolf Spawn Marker", "x": 4, "y": 1, "z": 7, "rx": 0, "ry": 45, "rz": 0, "sx": 1, "sy": 2, "sz": 1 },
    { "type": "add_component", "name": "Starter Wolf Spawn Marker", "component": "BoxCollider" },
    { "type": "create_material", "assetPath": "Assets/Resources/Codex/Materials/WolfMarker.mat", "r": 0.7, "g": 0.1, "b": 0.1, "a": 1 },
    { "type": "assign_material", "name": "Starter Wolf Spawn Marker", "assetPath": "Assets/Resources/Codex/Materials/WolfMarker.mat" },
    { "type": "create_csharp_script", "assetPath": "Assets/Scripts/CodexGenerated/QuestTrackerHud.cs", "overwrite": true, "content": "using UnityEngine;\\n\\npublic sealed class QuestTrackerHud : MonoBehaviour\\n{\\n    private void Start()\\n    {\\n        Debug.Log(\\\"Quest tracker HUD online.\\\");\\n    }\\n}\\n" },
    { "type": "create_canvas", "name": "Codex HUD Canvas", "width": 1920, "height": 1080 },
    { "type": "create_ui_panel", "name": "Quest Tracker Panel", "parent": "Codex HUD Canvas", "layout": "rightpanel", "marginRight": 32, "width": 360, "height": 420, "r": 0.03, "g": 0.04, "b": 0.05, "a": 0.85 },
    { "type": "create_ui_text", "name": "Quest Tracker Title", "parent": "Quest Tracker Panel", "text": "Frontier Orders", "fontSize": 28, "alignment": "UpperLeft", "anchorMinX": 0, "anchorMinY": 1, "anchorMaxX": 1, "anchorMaxY": 1, "pivotX": 0.5, "pivotY": 1, "anchoredX": 0, "anchoredY": -18, "width": -32, "height": 48, "r": 0.95, "g": 0.82, "b": 0.35, "a": 1 },
    { "type": "set_ui_text", "name": "Quest Tracker Title", "text": "Frontier Orders", "fontSize": 30, "alignment": "UpperLeft", "r": 1, "g": 0.85, "b": 0.35, "a": 1 },
    { "type": "set_ui_color", "name": "Quest Tracker Panel", "r": 0.03, "g": 0.04, "b": 0.05, "a": 0.9 },
    { "type": "set_ui_rect", "name": "Quest Tracker Panel", "layout": "rightpanel", "marginRight": 32, "width": 380, "height": 440 },
    { "type": "create_ui_button", "name": "Accept Quest Button", "parent": "Quest Tracker Panel", "text": "Accept", "fontSize": 22, "anchorMinX": 1, "anchorMinY": 0, "anchorMaxX": 1, "anchorMaxY": 0, "pivotX": 1, "pivotY": 0, "anchoredX": -18, "anchoredY": 18, "width": 132, "height": 44, "r": 0.14, "g": 0.32, "b": 0.45, "a": 1 },
    { "type": "create_folder", "assetPath": "Assets/Resources/Codex" },
    { "type": "open_scene", "scenePath": "Assets/Scenes/VoxelDemoScene.unity" },
    { "type": "save_scene" },
    { "type": "execute_menu_item", "menuPath": "GameObject/Create Empty" },
    { "type": "select_object", "name": "Wolf Spawn Marker" }
  ]
}

Allowed action types:
- create_game_object: creates an empty object or Unity primitive. primitive may be Cube, Sphere, Capsule, Cylinder, Plane, Quad.
- set_transform: sets position/rotation/scale for a named object.
- set_parent: parents a named scene object under another named scene object.
- rename_object: renames an existing scene object by name. Use newName for the target name.
- set_active: activates or deactivates an existing scene object with active true/false.
- destroy_object: removes an existing scene object through Unity Undo.
- add_component: adds a component by type name to a named object.
- create_canvas: creates a Screen Space Overlay Canvas with CanvasScaler and EventSystem support. Use this before UI.
- create_ui_panel: creates a UI RectTransform with Image under parent.
- create_ui_text: creates legacy UnityEngine.UI.Text under parent.
- create_ui_button: creates a UnityEngine.UI.Button with Image and child Text under parent.
- create_ui_image: creates a UnityEngine.UI.Image under parent.
- set_ui_text: edits an existing UnityEngine.UI.Text component's text/fontSize/alignment/color.
- set_ui_color: edits an existing UI Graphic color, or a Renderer material color if the object is not UI.
- set_ui_rect: edits an existing RectTransform's layout/anchors/pivot/position/size/rotation/scale.
- create_material: creates a Standard/Universal compatible material asset at assetPath with RGBA.
- assign_material: assigns a material asset to a named object's Renderer.
- create_csharp_script: writes a C# script under Assets/ with assetPath ending in .cs, content as the full source text, and optional overwrite true/false.
- create_folder: creates an Assets-relative folder.
- open_scene: opens a scene by asset path.
- save_scene: saves the active scene.
- execute_menu_item: runs a Unity editor menu item.
- select_object: selects a named scene object.

If the user asks for broad changes, propose concrete Unity actions.
The plan field must be a brief user-visible implementation summary, not hidden chain-of-thought. Keep each plan item short and action-oriented.
When the request refers to existing objects, prefer edit actions over recreating objects: set_transform, set_parent, rename_object, set_active, destroy_object, set_ui_text, set_ui_color, set_ui_rect, assign_material, or add_component.
Use create_* actions only when the target object/asset does not already exist or the user explicitly asks for a replacement.
For GUI/HUD/menu work, prefer create_canvas plus create_ui_panel/create_ui_text/create_ui_button/create_ui_image.
For GUI/HUD/menu revision work, prefer set_ui_text/set_ui_color/set_ui_rect on existing elements.
Always set parent for UI objects. Parent panels and controls under the Canvas or under another UI panel.
Use RectTransform fields for UI: parent, width, height, anchoredX, anchoredY, anchorMinX, anchorMinY, anchorMaxX, anchorMaxY, pivotX, pivotY.
Prefer layout presets for common screen placement: fullscreen, topbar, bottombar, leftpanel, rightpanel, center/modal.
For full-width bars, use layout topbar or bottombar instead of manually guessing anchors.
For side panels, use layout leftpanel or rightpanel plus width/height/margins.
For child controls inside a panel, use anchors relative to that parent and positive width/height; do not use negative width/height unless intentionally stretching.
Use alignment values like UpperLeft, MiddleCenter, LowerRight for UI text.
For new gameplay/editor helper code, use create_csharp_script with paths like Assets/Scripts/CodexGenerated/MyFeature.cs.
Generated C# must be complete, compile-ready Unity C# source. Include using directives. Prefer unique class names matching the file name.
Do not try to add a newly-created script component in the same action batch; Unity must import/compile the script first.
Keep batches under 64 actions unless the user explicitly asks for a large generation pass.
Use project-relative Unity asset paths starting with Assets/.
If image attachments are present, inspect them as screenshot/reference context for scene layout, GUI placement, asset style, or bug reports.
Do not copy image paths into generated Unity actions unless the user explicitly asks to create assets from those files.

Context:
- Unity project root: ${request.unityProjectRoot}
- Repo root: ${request.repoRoot}
- Active scene: ${request.activeScenePath}
- Selected objects: ${request.selectedObjects?.join(", ") ?? "(none)"}
- Existing scene objects: ${request.sceneObjects?.join(", ") ?? "(none)"}
- Known scenes: ${request.knownScenes?.join(", ") ?? "(none)"}
- Attached images: ${attachedImagePaths.length > 0 ? attachedImagePaths.join(", ") : "(none)"}

User request:
${request.prompt}`;

const codex = new Codex();
console.error("CODEX_STATUS Starting Codex SDK thread.");
const thread = codex.startThread({
  workingDirectory: request.repoRoot,
  skipGitRepoCheck: true,
  sandboxMode: "danger-full-access",
  approvalPolicy: "never",
  networkAccessEnabled: true,
  modelReasoningEffort: "medium",
});

console.error("CODEX_STATUS Waiting for Codex to plan Unity actions.");
if (attachedImagePaths.length > 0) {
  console.error(`CODEX_STATUS Sending ${attachedImagePaths.length} image attachment(s) to Codex.`);
}

const inputItems = [{ type: "text", text: prompt }];
for (const imagePath of attachedImagePaths) {
  inputItems.push({ type: "local_image", path: imagePath });
}

const result = await thread.run(inputItems);
console.error("CODEX_STATUS Codex response received; returning action JSON to Unity.");
process.stdout.write(result?.finalResponse ?? JSON.stringify(result));

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }

  return Buffer.concat(chunks).toString("utf8");
}
