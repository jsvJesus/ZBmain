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
    private const float ViewRadius = 42f;
    private const float TileSize = 24f;
    private const float CellHalfSize = 1f;
    private const int SamplesPerAxis = 3;

    public static VegetationBuildResult Build(
        DetailLevel details)
    {
        if (details.Models.Length == 0 ||
            details.Cells.Length == 0)
        {
            return Empty();
        }

        PreviewFocus focus = FindFocus(details);

        if (focus.Cells == 0)
        {
            VegetationBuildResult empty = Empty();

            VegetationPreviewDiagnostics.Write(
                details,
                empty,
                0,
                0);

            return empty;
        }

        var selected = new List<SelectedInstance>(
            MaximumInstances);

        var random = new Random(41091);

        int generated = 0;
        int processedCells = 0;

        foreach (DetailCell cell in details.Cells)
        {
            float height = GetHeight(cell);

            if (!float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y) ||
                !float.IsFinite(height))
            {
                continue;
            }

            float dx = cell.Position.X - focus.Center.X;
            float dz = cell.Position.Y - focus.Center.Z;

            if (dx * dx + dz * dz >
                ViewRadius * ViewRadius)
            {
                continue;
            }

            if (GetCellWeight(cell, details.Models) == 0)
            {
                continue;
            }

            processedCells++;

            AddLayer(cell, height, cell.Layer0);
            AddLayer(cell, height, cell.Layer1);
            AddLayer(cell, height, cell.Layer2);
            AddLayer(cell, height, cell.Layer3);
        }

        var groups = new List<VegetationInstance>[
            details.Models.Length
        ];

        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = new List<VegetationInstance>();
        }

        foreach (SelectedInstance selectedInstance in selected)
        {
            groups[selectedInstance.ModelIndex].Add(
                selectedInstance.Instance);
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

        var result = new VegetationBuildResult(
            focus.Center,
            batches.ToArray(),
            selected.Count);

        VegetationPreviewDiagnostics.Write(
            details,
            result,
            processedCells,
            generated);

        return result;

        void AddLayer(
            DetailCell cell,
            float height,
            DetailLayer layer)
        {
            if (GetLayerWeight(layer, details.Models) == 0)
            {
                return;
            }

            DetailModel model = details.Models[layer.Model];

            float minScale = MathF.Max(
                0.01f,
                MathF.Min(model.MinScale, model.MaxScale));

            float maxScale = MathF.Max(
                minScale,
                MathF.Max(model.MinScale, model.MaxScale));

            for (int z = 0; z < SamplesPerAxis; z++)
            {
                for (int x = 0; x < SamplesPerAxis; x++)
                {
                    float u = (
                        x + random.NextSingle()) /
                        SamplesPerAxis;

                    float v = (
                        z + random.NextSingle()) /
                        SamplesPerAxis;

                    float density = SampleDensity(
                        layer.Density,
                        u,
                        v);

                    if (random.NextSingle() >= density)
                    {
                        continue;
                    }

                    float worldX = cell.Position.X +
                        (u * 2f - 1f) * CellHalfSize;

                    float worldZ = cell.Position.Y +
                        (v * 2f - 1f) * CellHalfSize;

                    float scale = minScale +
                        (maxScale - minScale) *
                        random.NextSingle();

                    float rotation =
                        random.NextSingle() * MathF.Tau;

                    float variation = 0.85f +
                        random.NextSingle() * 0.25f;

                    var instance = new VegetationInstance(
                        new Vector4(
                            worldX,
                            height,
                            worldZ,
                            scale),
                        new Vector4(
                            variation,
                            variation * 0.96f,
                            variation * 0.89f,
                            rotation));

                    generated++;

                    var candidate = new SelectedInstance(
                        layer.Model,
                        instance);

                    if (selected.Count < MaximumInstances)
                    {
                        selected.Add(candidate);
                        continue;
                    }

                    int replacement = random.Next(generated);

                    if (replacement < MaximumInstances)
                    {
                        selected[replacement] = candidate;
                    }
                }
            }
        }
    }

    private static PreviewFocus FindFocus(
        DetailLevel details)
    {
        var tiles = new Dictionary<
            (int X, int Z),
            TileAccumulator>();

        foreach (DetailCell cell in details.Cells)
        {
            if (!float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y))
            {
                continue;
            }

            float height = GetHeight(cell);

            if (!float.IsFinite(height))
            {
                continue;
            }

            int weight = GetCellWeight(
                cell,
                details.Models);

            if (weight == 0)
            {
                continue;
            }

            int tileX = (int)MathF.Floor(
                cell.Position.X / TileSize);

            int tileZ = (int)MathF.Floor(
                cell.Position.Y / TileSize);

            var key = (tileX, tileZ);

            if (!tiles.TryGetValue(
                    key,
                    out TileAccumulator? tile))
            {
                tile = new TileAccumulator();
                tiles.Add(key, tile);
            }

            tile.Add(
                cell.Position.X,
                height,
                cell.Position.Y,
                weight);
        }

        TileAccumulator? best = null;

        foreach (TileAccumulator tile in tiles.Values)
        {
            if (best is null ||
                tile.Weight > best.Weight)
            {
                best = tile;
            }
        }

        if (best is null)
        {
            return new PreviewFocus(
                Vector3.Zero,
                0);
        }

        return new PreviewFocus(
            best.GetCenter(),
            best.Cells);
    }

    private static int GetCellWeight(
        DetailCell cell,
        DetailModel[] models)
    {
        return
            GetLayerWeight(cell.Layer0, models) +
            GetLayerWeight(cell.Layer1, models) +
            GetLayerWeight(cell.Layer2, models) +
            GetLayerWeight(cell.Layer3, models);
    }

    private static int GetLayerWeight(
        DetailLayer layer,
        DetailModel[] models)
    {
        if (layer.Model == byte.MaxValue ||
            layer.Model >= models.Length ||
            layer.Density == 0)
        {
            return 0;
        }

        DetailModel model = models[layer.Model];

        if (model.Vertices.Length == 0 ||
            model.Indices.Length < 3 ||
            !float.IsFinite(model.MinScale) ||
            !float.IsFinite(model.MaxScale) ||
            MathF.Max(model.MinScale, model.MaxScale) <= 0f)
        {
            return 0;
        }

        ushort density = layer.Density;

        return
            (density & 0xF) +
            ((density >> 4) & 0xF) +
            ((density >> 8) & 0xF) +
            ((density >> 12) & 0xF);
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

    private static float GetHeight(DetailCell cell)
    {
        return float.IsFinite(cell.BaseHeight)
            ? cell.BaseHeight
            : cell.TopHeight;
    }

    private static VegetationBuildResult Empty()
    {
        return new VegetationBuildResult(
            Vector3.Zero,
            [],
            0);
    }

    private readonly record struct PreviewFocus(
        Vector3 Center,
        int Cells);

    private readonly record struct SelectedInstance(
        int ModelIndex,
        VegetationInstance Instance);

    private sealed class TileAccumulator
    {
        private double _sumX;
        private double _sumY;
        private double _sumZ;

        public long Weight { get; private set; }
        public int Cells { get; private set; }

        public void Add(
            float x,
            float y,
            float z,
            int weight)
        {
            _sumX += x * (double)weight;
            _sumY += y * (double)weight;
            _sumZ += z * (double)weight;

            Weight += weight;
            Cells++;
        }

        public Vector3 GetCenter()
        {
            if (Weight == 0)
            {
                return Vector3.Zero;
            }

            return new Vector3(
                (float)(_sumX / Weight),
                (float)(_sumY / Weight),
                (float)(_sumZ / Weight));
        }
    }
}
