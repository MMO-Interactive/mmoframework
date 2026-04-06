using System;

namespace VoxelLibrary;

public static class VoxelBrushes
{
    public static void AddSphere(VoxelWorld world, Float3 center, float radius, ushort materialId, float feather = 1f)
    {
        ModifySphere(world, center, radius, feather, sample =>
            new VoxelSample(MathF.Max(sample.Density, 1f), materialId));
    }

    public static void SubtractSphere(VoxelWorld world, Float3 center, float radius, float feather = 1f)
    {
        ModifySphere(world, center, radius, feather, _ => VoxelSample.Air);
    }

    public static void PaintSphere(VoxelWorld world, Float3 center, float radius, ushort materialId)
    {
        var min = world.WorldToGridPosition(new Float3(center.X - radius, center.Y - radius, center.Z - radius));
        var max = world.WorldToGridPosition(new Float3(center.X + radius, center.Y + radius, center.Z + radius));

        for (var z = min.Z; z <= max.Z; z++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    var worldPosition = world.GridToWorldPosition(x, y, z);
                    if ((worldPosition - center).LengthSquared > radius * radius)
                    {
                        continue;
                    }

                    var sample = world.GetSampleGlobal(x, y, z);
                    if (sample.IsSolid())
                    {
                        world.SetSampleGlobal(x, y, z, sample with { MaterialId = materialId });
                    }
                }
            }
        }
    }

    private static void ModifySphere(VoxelWorld world, Float3 center, float radius, float feather, Func<VoxelSample, VoxelSample> operation)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Brush radius must be positive.");
        }

        var effectiveFeather = MathF.Max(feather, world.VoxelSize);
        var min = world.WorldToGridPosition(new Float3(center.X - radius - effectiveFeather, center.Y - radius - effectiveFeather, center.Z - radius - effectiveFeather));
        var max = world.WorldToGridPosition(new Float3(center.X + radius + effectiveFeather, center.Y + radius + effectiveFeather, center.Z + radius + effectiveFeather));

        for (var z = min.Z; z <= max.Z; z++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    var position = world.GridToWorldPosition(x, y, z);
                    var distance = (position - center).Length;
                    var signedDistance = radius - distance;
                    if (signedDistance < -effectiveFeather)
                    {
                        continue;
                    }

                    var current = world.GetSampleGlobal(x, y, z);
                    var target = operation(current);
                    var blend = Math.Clamp((signedDistance + effectiveFeather) / effectiveFeather, 0f, 1f);
                    var density = MathF.Max(current.Density, Lerp(-1f, target.Density, blend));
                    if (!target.IsSolid() && signedDistance <= 0f)
                    {
                        density = MathF.Min(current.Density, Lerp(current.Density, -1f, 1f - blend));
                    }

                    var materialId = target.MaterialId != 0 ? target.MaterialId : current.MaterialId;
                    world.SetSampleGlobal(x, y, z, new VoxelSample(density, materialId));
                }
            }
        }
    }

    private static float Lerp(float start, float end, float amount)
        => start + ((end - start) * amount);
}
