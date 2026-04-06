using System;

namespace VoxelLibrary;

public readonly record struct Float3(float X, float Y, float Z)
{
    public static Float3 Zero => new(0f, 0f, 0f);

    public float LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    public float Length => MathF.Sqrt(LengthSquared);

    public Float3 Normalized()
    {
        var length = Length;
        return length <= 1e-6f ? Zero : this / length;
    }

    public static float Dot(Float3 left, Float3 right)
        => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    public static Float3 Cross(Float3 left, Float3 right)
        => new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    public static Float3 Lerp(Float3 start, Float3 end, float amount)
        => start + ((end - start) * amount);

    public static Float3 operator +(Float3 left, Float3 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Float3 operator -(Float3 left, Float3 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Float3 operator *(Float3 value, float scalar)
        => new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    public static Float3 operator /(Float3 value, float scalar)
        => new(value.X / scalar, value.Y / scalar, value.Z / scalar);
}
