using Microsoft.Xna.Framework;

namespace MonoGameEngine.Editor.Mmorpg;

public sealed record NpcSpawnPoint(string NpcArchetype, int Level, Vector2 Position);
