using System.Numerics;
using Core.Levels;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationChunk
{
    public const int TileSize = 32;

    private const int SamplesPerAxis = 3;
    private const int MaximumInstances = 12_000;
    private const float CellHalfSize = 1f;

    public int TileX { get; }
    public int TileZ { get; }

    public Vector3 Center { get; }
    public float Radius { get; }

    public VegetationBatch[] Batches { get; }
    public int InstanceCount { get; }
    public int MissingHeights { get; }

    private VegetationChunk(
        int tileX,
        int tileZ,
        Vector3 center,
        float radius,
        VegetationBatch[] batches,
        int instanceCount,
        int missingHeights)
    {
        TileX = tileX;
        TileZ = tileZ;
        Center = center;
        Radius = radius;
        Batches = batches;
        InstanceCount = instanceCount;
        MissingHeights = missingHeights;
    }

    public static VegetationChunk Build(
        int tileX,
        int tileZ,
        IReadOnlyList<DetailCell> cells,
        DetailLevel details,
        TerrainHeightSampler? terrain)
    {
        var groups = new List<VegetationInstance>[
            details.Models.Length
        ];

        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = new List<VegetationInstance>();
        }

        int totalInstances = 0;
        int missingHeights = 0;

        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;

        foreach (DetailCell cell in cells)
        {
            if (!float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y))
            {
                continue;
            }

            AddLayer(cell, cell.Layer0, 0);
            AddLayer(cell, cell.Layer1, 1);
            AddLayer(cell, cell.Layer2, 2);
            AddLayer(cell, cell.Layer3, 3);
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

        float middleY = 0f;
        float verticalRadius = 0f;

        if (float.IsFinite(minimumY) &&
            float.IsFinite(maximumY))
        {
            middleY = (minimumY + maximumY) * 0.5f;
            verticalRadius =
                (maximumY - minimumY) * 0.5f;
        }

        var center = new Vector3(
            (tileX + 0.5f) * TileSize,
            middleY,
            (tileZ + 0.5f) * TileSize);

        float horizontalRadius =
            TileSize * 0.75f;

        float radius = MathF.Sqrt(
            horizontalRadius * horizontalRadius +
            verticalRadius * verticalRadius) + 8f;

        return new VegetationChunk(
            tileX,
            tileZ,
            center,
            radius,
            batches.ToArray(),
            totalInstances,
            missingHeights);

        void AddLayer(
            DetailCell cell,
            DetailLayer layer,
            int layerIndex)
        {
            if (totalInstances >= MaximumInstances ||
                layer.Model == byte.MaxValue ||
                layer.Model >= details.Models.Length ||
                layer.Density == 0)
            {
                return;
            }

            DetailModel model = details.Models[layer.Model];

            if (model.Vertices.Length == 0 ||
                model.Indices.Length < 3 ||
                !float.IsFinite(model.MinScale) ||
                !float.IsFinite(model.MaxScale))
            {
                return;
            }

            float minScale = MathF.Max(
                0.01f,
                MathF.Min(
                    model.MinScale,
                    model.MaxScale));

            float maxScale = MathF.Max(
                minScale,
                MathF.Max(
                    model.MinScale,
                    model.MaxScale));

            uint seed = 2166136261u;

            unchecked
            {
                seed = (seed ^ (uint)
                    BitConverter.SingleToInt32Bits(
                        cell.Position.X)) * 16777619u;

                seed = (seed ^ (uint)
                    BitConverter.SingleToInt32Bits(
                        cell.Position.Y)) * 16777619u;

                seed = (seed ^ (uint)layerIndex) *
                    16777619u;

                seed = (seed ^ layer.Model) *
                    16777619u;
            }

            var random = new Random(unchecked((int)seed));

            for (int z = 0; z < SamplesPerAxis; z++)
            {
                for (int x = 0; x < SamplesPerAxis; x++)
                {
                    if (totalInstances >= MaximumInstances)
                    {
                        return;
                    }

                    float u =
                        (x + random.NextSingle()) /
                        SamplesPerAxis;

                    float v =
                        (z + random.NextSingle()) /
                        SamplesPerAxis;

                    float density = SampleDensity(
                        layer.Density,
                        u,
                        v);

                    if (random.NextSingle() >= density)
                    {
                        continue;
                    }

                    float worldX =
                        cell.Position.X +
                        (u * 2f - 1f) * CellHalfSize;

                    float worldZ =
                        cell.Position.Y +
                        (v * 2f - 1f) * CellHalfSize;

                    float worldY;

                    if (terrain is not null)
                    {
                        if (!terrain.TryGetHeight(
                                worldX,
                                worldZ,
                                cell.BaseHeight,
                                cell.TopHeight,
                                out worldY))
                        {
                            missingHeights++;
                            continue;
                        }
                    }
                    else
                    {
                        worldY = float.IsFinite(
                            cell.BaseHeight)
                            ? cell.BaseHeight
                            : cell.TopHeight;

                        if (!float.IsFinite(worldY))
                        {
                            missingHeights++;
                            continue;
                        }
                    }

                    float scale =
                        minScale +
                        (maxScale - minScale) *
                        random.NextSingle();

                    float rotation =
                        random.NextSingle() * MathF.Tau;

                    float variation =
                        0.85f +
                        random.NextSingle() * 0.25f;

                    var instance = new VegetationInstance(
                        new Vector4(
                            worldX,
                            worldY,
                            worldZ,
                            scale),
                        new Vector4(
                            variation,
                            variation * 0.96f,
                            variation * 0.89f,
                            rotation));

                    groups[layer.Model].Add(instance);

                    totalInstances++;

                    minimumY = MathF.Min(
                        minimumY,
                        worldY);

                    maximumY = MathF.Max(
                        maximumY,
                        worldY + maxScale * 4f);
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
}
