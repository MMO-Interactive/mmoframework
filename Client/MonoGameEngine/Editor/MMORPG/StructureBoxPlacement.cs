using Microsoft.Xna.Framework;

namespace MonoGameEngine.Editor.Mmorpg;

public sealed record StructureBoxPlacement(Point Min, Point Max, int BaseY, int TopY, ushort MaterialId);
