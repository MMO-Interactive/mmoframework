using MonoGameEngine.Editor.Mmorpg;

namespace MonoGameMmorpgEditor.Ai;

public sealed record MmorpgEditorContext(
    ZoneDefinition ActiveZone,
    int NpcSpawnCount,
    int ResourceSpawnCount,
    float TerrainHeightScale);
