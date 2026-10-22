using System.Numerics;
using Core.Levels;

namespace Editor.Rendering.Direct3D11;

internal readonly record struct VegetationInstance(
    Vector4 PositionScale,
    Vector4 TintPhase);

internal static class VegetationInstanceBuilder
{
    private const int MaximumInstances = 45_000;
    private const float ViewRadius = 120f;

    public static VegetationInstance[] Build(
        DetailLevel details)
    {
        if (details.Models.Length == 0 ||
            details.Cells.Length == 0)
        {
            return [];
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        foreach (DetailCell cell in details.Cells)
        {
            if (!HasVegetation(cell))
            {
                continue;
            }

            minX = MathF.Min(minX, cell.Position.X);
            maxX = MathF.Max(maxX, cell.Position.X);

            minZ = MathF.Min(minZ, cell.Position.Y);
            maxZ = MathF.Max(maxZ, cell.Position.Y);

            if (float.IsFinite(cell.TopHeight))
            {
                minY = MathF.Min(minY, cell.TopHeight);
                maxY = MathF.Max(maxY, cell.TopHeight);
            }
        }

        if (!float.IsFinite(minX) ||
            !float.IsFinite(minY))
        {
            return [];
        }

        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;

        var instances = new List<VegetationInstance>(
            MaximumInstances);

        var random = new Random(41091);

        int generated = 0;

        foreach (DetailCell cell in details.Cells)
        {
            if (MathF.Abs(cell.Position.X - centerX) > ViewRadius ||
                MathF.Abs(cell.Position.Y - centerZ) > ViewRadius ||
                !float.IsFinite(cell.TopHeight))
            {
                continue;
            }

            AddLayer(cell, cell.Layer0);
            AddLayer(cell, cell.Layer1);
            AddLayer(cell, cell.Layer2);
            AddLayer(cell, cell.Layer3);
        }

        return instances.ToArray();

        void AddLayer(
            DetailCell cell,
            DetailLayer layer)
        {
            if (layer.Model == byte.MaxValue ||
                layer.Model >= details.Models.Length ||
                layer.Density == 0)
            {
                return;
            }

            int density =
                (layer.Density & 0xF) +
                ((layer.Density >> 4) & 0xF) +
                ((layer.Density >> 8) & 0xF) +
                ((layer.Density >> 12) & 0xF);

            if (density <= 0)
            {
                return;
            }

            int count = 1 + density / 25;

            DetailModel model = details.Models[layer.Model];

            float minScale = MathF.Max(
                0.1f,
                MathF.Min(model.MinScale, model.MaxScale));

            float maxScale = MathF.Max(
                minScale,
                MathF.Max(model.MinScale, model.MaxScale));

            for (int i = 0; i < count; i++)
            {
                float jitterX =
                    (random.NextSingle() - 0.5f) * 2f;

                float jitterZ =
                    (random.NextSingle() - 0.5f) * 2f;

                float x =
                    (cell.Position.X + jitterX - centerX) /
                    ViewRadius;

                float z =
                    (cell.Position.Y + jitterZ - centerZ) /
                    ViewRadius;

                float y = Math.Clamp(
                    (cell.TopHeight - centerY) / 80f,
                    -1f,
                    1f);

                float scaleFactor =
                    minScale +
                    (maxScale - minScale) *
                    random.NextSingle();

                float size =
                    (0.012f + random.NextSingle() * 0.01f) *
                    scaleFactor;

                float variation = random.NextSingle();

                Vector3 tint = (layer.Model % 4) switch
                {
                    0 => new Vector3(0.28f, 0.67f, 0.22f),
                    1 => new Vector3(0.40f, 0.64f, 0.20f),
                    2 => new Vector3(0.31f, 0.55f, 0.18f),
                    _ => new Vector3(0.46f, 0.64f, 0.27f)
                };

                tint *= 0.8f + variation * 0.35f;

                var instance = new VegetationInstance(
                    new Vector4(x, y, z, size),
                    new Vector4(
                        tint,
                        random.NextSingle() * MathF.Tau));

                generated++;

                if (instances.Count < MaximumInstances)
                {
                    instances.Add(instance);
                    continue;
                }

                // Резервуарная выборка: не заполняем
                // лимит экземплярами только из первых строк карты.
                int index = random.Next(generated);

                if (index < MaximumInstances)
                {
                    instances[index] = instance;
                }
            }
        }
    }

    private static bool HasVegetation(DetailCell cell)
    {
        return IsActive(cell.Layer0) ||
               IsActive(cell.Layer1) ||
               IsActive(cell.Layer2) ||
               IsActive(cell.Layer3);
    }

    private static bool IsActive(DetailLayer layer)
    {
        return layer.Model != byte.MaxValue &&
               layer.Density != 0;
    }
}
