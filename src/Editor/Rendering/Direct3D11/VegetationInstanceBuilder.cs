using System.Numerics;
using System.Runtime.InteropServices;
using Core.Levels;

namespace Editor.Rendering.Direct3D11;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct VegetationInstance(
    Vector4 PositionScale,
    Vector4 TintRotation);

internal readonly record struct VegetationBatch(
    int ModelIndex,
    VegetationInstance[] Instances);

internal sealed record VegetationBuildResult(
    Vector3 Center,
    VegetationBatch[] Batches,
    int InstanceCount);

internal static class VegetationInstanceBuilder
{
    private const int MaximumInstances = 45_000;
    private const float ViewRadius = 120f;
    private const int SamplesPerAxis = 3;
    private const float CellHalfSize = 1f;

    public static VegetationBuildResult Build(DetailLevel details)
    {
        if (details.Models.Length == 0 ||
            details.Cells.Length == 0)
        {
            return Empty();
        }

        Vector2 center = FindCenter(details);

        var selected = new List<SelectedInstance>(
            MaximumInstances);

        var random = new Random(41091);

        int generated = 0;

        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;

        foreach (DetailCell cell in details.Cells)
        {
            if (!float.IsFinite(cell.TopHeight) ||
                !float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y))
            {
                continue;
            }

            float distanceX = cell.Position.X - center.X;
            float distanceZ = cell.Position.Y - center.Y;

            if (distanceX * distanceX +
                distanceZ * distanceZ >
                ViewRadius * ViewRadius)
            {
                continue;
            }

            if (!HasVegetation(cell))
            {
                continue;
            }

            minimumY = MathF.Min(
                minimumY,
                cell.TopHeight);

            maximumY = MathF.Max(
                maximumY,
                cell.TopHeight);

            AddLayer(cell, cell.Layer0);
            AddLayer(cell, cell.Layer1);
            AddLayer(cell, cell.Layer2);
            AddLayer(cell, cell.Layer3);
        }

        var groups = new List<VegetationInstance>[
            details.Models.Length
        ];

        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = new List<VegetationInstance>();
        }

        foreach (SelectedInstance item in selected)
        {
            groups[item.ModelIndex].Add(item.Instance);
        }

        var batches = new List<VegetationBatch>();

        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].Count == 0)
            {
                continue;
            }

            batches.Add(new VegetationBatch(
                i,
                groups[i].ToArray()));
        }

        float centerY = float.IsFinite(minimumY)
            ? (minimumY + maximumY) * 0.5f
            : 0f;

        return new VegetationBuildResult(
            new Vector3(center.X, centerY, center.Y),
            batches.ToArray(),
            selected.Count);

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

            DetailModel model = details.Models[layer.Model];

            if (model.Vertices.Length == 0 ||
                model.Indices.Length < 3)
            {
                return;
            }

            float minimumScale = MathF.Min(
                model.MinScale,
                model.MaxScale);

            float maximumScale = MathF.Max(
                model.MinScale,
                model.MaxScale);

            if (!float.IsFinite(minimumScale) ||
                !float.IsFinite(maximumScale) ||
                maximumScale <= 0f)
            {
                return;
            }

            minimumScale = MathF.Max(
                0.01f,
                minimumScale);

            maximumScale = MathF.Max(
                minimumScale,
                maximumScale);

            for (int z = 0; z < SamplesPerAxis; z++)
            {
                for (int x = 0; x < SamplesPerAxis; x++)
                {
                    float u = (x + random.NextSingle()) /
                              SamplesPerAxis;

                    float v = (z + random.NextSingle()) /
                              SamplesPerAxis;

                    float density = SampleDensity(
                        layer.Density,
                        u,
                        v);

                    if (random.NextSingle() > density)
                    {
                        continue;
                    }

                    float worldX = cell.Position.X +
                        (u * 2f - 1f) * CellHalfSize;

                    float worldZ = cell.Position.Y +
                        (v * 2f - 1f) * CellHalfSize;

                    float scale = minimumScale +
                        (maximumScale - minimumScale) *
                        random.NextSingle();

                    float rotation = random.NextSingle() *
                        MathF.Tau;

                    float variation = 0.82f +
                        random.NextSingle() * 0.28f;

                    var instance = new VegetationInstance(
                        new Vector4(
                            worldX,
                            cell.TopHeight,
                            worldZ,
                            scale),
                        new Vector4(
                            variation,
                            variation * 0.95f,
                            variation * 0.84f,
                            rotation));

                    generated++;

                    var item = new SelectedInstance(
                        layer.Model,
                        instance);

                    if (selected.Count < MaximumInstances)
                    {
                        selected.Add(item);
                        continue;
                    }

                    int replacement = random.Next(generated);

                    if (replacement < MaximumInstances)
                    {
                        selected[replacement] = item;
                    }
                }
            }
        }
    }

    private static float SampleDensity(
        ushort density,
        float u,
        float v)
    {
        float d00 = (density & 0xF) / 15f;
        float d10 = ((density >> 4) & 0xF) / 15f;
        float d01 = ((density >> 8) & 0xF) / 15f;
        float d11 = ((density >> 12) & 0xF) / 15f;

        float bottom = d00 + (d10 - d00) * u;
        float top = d01 + (d11 - d01) * u;

        return Math.Clamp(
            bottom + (top - bottom) * v,
            0f,
            1f);
    }

    private static Vector2 FindCenter(DetailLevel details)
    {
        float minimumX = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;
        float minimumZ = float.PositiveInfinity;
        float maximumZ = float.NegativeInfinity;

        foreach (DetailCell cell in details.Cells)
        {
            if (!HasVegetation(cell) ||
                !float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y))
            {
                continue;
            }

            minimumX = MathF.Min(
                minimumX,
                cell.Position.X);

            maximumX = MathF.Max(
                maximumX,
                cell.Position.X);

            minimumZ = MathF.Min(
                minimumZ,
                cell.Position.Y);

            maximumZ = MathF.Max(
                maximumZ,
                cell.Position.Y);
        }

        if (!float.IsFinite(minimumX))
        {
            return Vector2.Zero;
        }

        return new Vector2(
            (minimumX + maximumX) * 0.5f,
            (minimumZ + maximumZ) * 0.5f);
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

    private static VegetationBuildResult Empty()
    {
        return new VegetationBuildResult(
            Vector3.Zero,
            [],
            0);
    }

    private readonly record struct SelectedInstance(
        int ModelIndex,
        VegetationInstance Instance);
}
