using Microsoft.Xna.Framework;

namespace MonoGameEngine.Editor;

public abstract record EditorAction;

public sealed record SetPlayerSpeedAction(float Speed) : EditorAction;

public sealed record SetPlayerColorAction(Color Color) : EditorAction;

public sealed record SpawnMarkerAction(Vector2 Position) : EditorAction;

public sealed record ClearMarkersAction() : EditorAction;

public sealed record SetTerrainHeightScaleAction(float HeightScale) : EditorAction;

public sealed record RegenerateTerrainAction() : EditorAction;

public sealed record SetZoneAction(string ZoneId) : EditorAction;

public sealed record AddNpcSpawnAction(string NpcArchetype, int Level, Vector2 Position) : EditorAction;

public sealed record AddResourceSpawnAction(string ResourceType, Vector2 Position) : EditorAction;

public sealed record PlaceStructureBoxAction(Point Min, Point Max, int BaseY, int TopY, ushort MaterialId) : EditorAction;

public sealed record SaveWorkspaceAction(string RelativePath) : EditorAction;

public sealed record LoadWorkspaceAction(string RelativePath) : EditorAction;

