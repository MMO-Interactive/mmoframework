# MonoGameEngine

A MonoGame-based editor/runtime scaffold integrated with this MMO framework's shared terrain systems.

## MMO-framework integration

This editor now integrates directly with `Shared/VoxelLibrary`:

- Uses `AdaptiveTerrainGenerator` in `Hybrid` mode as the terrain backend for editor terrain views.
- Supports placement of voxel structure boxes through the same terrain generator APIs used by the framework.
- Maintains MMORPG authoring state (`zone`, `npc spawns`, `resource spawns`) in a workspace object designed for content tooling.

## Key systems

- `Editor/MMORPG/MmorpgEditorWorkspace.cs`
  - Holds active zone metadata.
  - Holds spawn tables for NPCs/resources.
  - Owns `AdaptiveTerrainGenerator` and terrain regeneration.
- `Terrain/TileTerrainRenderSystem.cs`
  - Renders a Wurm-like tile terrain mesh sampled from the MMO terrain generator.
- `Editor/AiFirstEditorSystem.cs`
  - Applies AI-style editor commands into workspace + runtime state.

## AI editor command examples (MMORPG-focused)

- `zone westfall`
- `add npc wolf level 5 at 340, 180`
- `add resource iron_vein at 420, 210`
- `build box 120,120,132,132 base 2 top 10 material 14`
- `terrain height 1.4`
- `regenerate terrain`
- `save workspace zones/westfall`
- `load workspace zones/westfall`

Workspace save files are written under `EditorData/` next to the built executable.

## Run

```bash
dotnet run --project Client/MonoGameEngine/MonoGameEngine.csproj
```

## Dedicated MMORPG Editor

The standalone AI-first MMORPG editor lives in `Client/MonoGameMmorpgEditor`.
Run it with:

```bash
dotnet run --project Client/MonoGameMmorpgEditor/MonoGameMmorpgEditor.csproj
```

## Controls

- Move square: `WASD` or arrow keys
- Orbit camera: `Left` / `Right`
- Tilt camera: `Up` / `Down`
- Zoom camera: `Q` / `E`
- Regenerate terrain: `R`
- Toggle editor: `Tab`
- Submit prompt: `Enter`
- Quit: `Esc`
