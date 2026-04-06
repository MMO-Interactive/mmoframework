namespace VoxelLibrary;

public readonly record struct Int3(int X, int Y, int Z)
{
    public static Int3 Zero => new(0, 0, 0);

    public static Int3 operator +(Int3 left, Int3 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Int3 operator -(Int3 left, Int3 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Int3 operator *(Int3 value, int scalar)
        => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
}
