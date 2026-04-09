# Unity Editor Codex Integration (MMO)

This folder adds a command bridge so Codex can control more of the Unity editor directly.

## Menu

- `MMO/Codex/Command Console`
- `MMO/Codex/Print Capabilities`

## Supported command actions

- `open_scene`
- `open_scene_additive`
- `create_scene_asset`
- `close_scene`
- `create_gameobject`
- `create_primitive`
- `delete_gameobject`
- `duplicate_gameobject`
- `rename_gameobject`
- `set_active`
- `instantiate_prefab`
- `set_transform`
- `reset_transform`
- `add_component`
- `set_component_property`
- `get_component_property`
- `remove_component`
- `select_object`
- `set_tag_layer`
- `create_folder`
- `move_to_parent`
- `list_root_objects`
- `list_children`
- `create_material`
- `assign_material`
- `focus_scene_view`
- `run_menu_item`
- `set_play_mode`
- `capture_scene_view_screenshot`
- `capture_game_view_screenshot`
- `get_transform_info`
- `list_components`
- `find_objects_by_name`
- `find_objects_by_component`
- `list_scene_hierarchy`
- `create_prefab_from_object`
- `list_open_scenes`
- `find_missing_scripts`
- `select_asset`
- `get_active_scene_info`
- `save_active_scene`
- `mark_scene_dirty`
- `undo_last`
- `redo_last`
- `save_project`

## Batch mode (multi-step commands)

The command console accepts a batch payload with `commands`, optional `stopOnError`, and optional `captureAfterEachStep`.

```json
{
  "stopOnError": true,
  "captureAfterEachStep": true,
  "screenshotFolder": "CodexScreenshots",
  "commands": [
    { "action": "open_scene", "path": "Assets/Scenes/WorldScene.unity" },
    { "action": "create_primitive", "primitiveType": "Capsule", "name": "NpcMarker", "parentPath": "World" },
    { "action": "save_project" }
  ]
}
```

## Screenshot examples

```json
{
  "action": "capture_scene_view_screenshot",
  "outputPath": "CodexScreenshots/scene_view.png",
  "width": 1280,
  "height": 720
}
```

```json
{
  "action": "capture_game_view_screenshot",
  "outputPath": "CodexScreenshots/game_view.png",
  "superSize": 1
}
```

## Query + prefab examples

```json
{
  "action": "find_objects_by_name",
  "name": "Npc"
}
```

```json
{
  "action": "open_scene_additive",
  "path": "Assets/Scenes/Lighting.unity"
}
```

```json
{
  "action": "create_scene_asset",
  "name": "EncounterPrototype",
  "parentAssetFolder": "Assets/Scenes"
}
```

```json
{
  "action": "close_scene",
  "path": "Assets/Scenes/Lighting.unity",
  "removeSceneObjects": true
}
```

```json
{
  "action": "find_objects_by_component",
  "componentType": "UnityEngine.Light"
}
```

```json
{
  "action": "list_scene_hierarchy",
  "maxDepth": 3
}
```

```json
{
  "action": "get_component_property",
  "path": "World/NpcMarker",
  "componentType": "UnityEngine.Transform",
  "propertyName": "m_LocalPosition"
}
```

```json
{
  "action": "remove_component",
  "path": "World/NpcMarker",
  "componentType": "UnityEngine.Light"
}
```

```json
{
  "action": "find_missing_scripts"
}
```

```json
{
  "action": "list_open_scenes"
}
```

```json
{
  "action": "select_asset",
  "path": "Assets/Prefabs/NpcMarker.prefab"
}
```

```json
{
  "action": "undo_last"
}
```

```json
{
  "action": "redo_last"
}
```

```json
{
  "action": "mark_scene_dirty"
}
```

```json
{
  "action": "save_active_scene"
}
```

```json
{
  "action": "get_active_scene_info"
}
```

```json
{
  "action": "create_prefab_from_object",
  "path": "World/NpcMarker",
  "prefabAssetPath": "Assets/Prefabs/NpcMarker.prefab"
}
```
