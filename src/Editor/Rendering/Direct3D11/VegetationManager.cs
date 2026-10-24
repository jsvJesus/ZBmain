using System.Diagnostics;
using System.Numerics;
using Core.Levels;
using Vortice.Direct3D11;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationManager : IDisposable
{
    private const float LoadRadius = 128f;
    private const float UnloadRadius = 176f;

    private readonly ID3D11Device _device;
    private readonly DetailLevel _details;
    private readonly TerrainHeightSampler? _terrain;

    private readonly Dictionary<
        (int X, int Z),
        List<DetailCell>> _cells = new();

    private readonly Dictionary<
        (int X, int Z),
        ResidentChunk> _loaded = new();

    private readonly HashSet<(int X, int Z)> _rejected =
        new();

    private readonly List<(int X, int Z)> _unloadKeys =
        new();

    private readonly VegetationGpuModel?[] _models;

    private Task<VegetationChunk>? _building;
    private (int X, int Z) _buildingKey;

    private bool _disposed;

    public int LoadedChunks => _loaded.Count;

    public int LoadedInstances =>
        _loaded.Values.Sum(chunk => chunk.Data.InstanceCount);

    public VegetationManager(
        ID3D11Device device,
        DetailLevel details,
        TerrainHeightSampler? terrain)
    {
        _device = device;
        _details = details;
        _terrain = terrain;

        _models = new VegetationGpuModel?[details.Models.Length];

        try
        {
            for (int i = 0; i < details.Models.Length; i++)
            {
                DetailModel model = details.Models[i];

                if (model.Vertices.Length == 0 ||
                    model.Indices.Length < 3)
                {
                    continue;
                }

                _models[i] = new VegetationGpuModel(
                    device,
                    model);
            }

            IndexCells();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Update(Vector3 cameraPosition)
    {
        if (_disposed)
        {
            return;
        }

        UnloadDistant(cameraPosition);
        CompletePendingBuild(cameraPosition);

        if (_building is not null)
        {
            return;
        }

        (int X, int Z)? candidate =
            FindNearestMissing(cameraPosition);

        if (!candidate.HasValue)
        {
            return;
        }

        (int X, int Z) key = candidate.Value;

        IReadOnlyList<DetailCell> cells = _cells[key];

        _buildingKey = key;

        _building = Task.Run(() =>
            VegetationChunk.Build(
                key.X,
                key.Z,
                cells,
                _details,
                _terrain));
    }

    public void Draw(
        ID3D11DeviceContext context,
        VegetationTextureCache textures,
        Matrix4x4 viewProjection)
    {
        var frustum = new ViewportFrustum(
            viewProjection);

        foreach (ResidentChunk resident in _loaded.Values)
        {
            if (!frustum.IntersectsSphere(
                    resident.Data.Center,
                    resident.Data.Radius))
            {
                continue;
            }

            foreach (VegetationGpuBatch batch
                     in resident.Batches)
            {
                context.PSSetShaderResource(
                    0,
                    textures.Get(batch.TextureName));

                batch.Draw(context);
            }
        }
    }

    private void CompletePendingBuild(
        Vector3 cameraPosition)
    {
        Task<VegetationChunk>? task = _building;

        if (task is null || !task.IsCompleted)
        {
            return;
        }

        (int X, int Z) key = _buildingKey;

        _building = null;

        if (task.IsFaulted || task.IsCanceled)
        {
            _rejected.Add(key);

            Trace.WriteLine(
                $"Vegetation chunk {key}: " +
                task.Exception?.GetBaseException().Message);

            return;
        }

        VegetationChunk chunk = task.Result;

        if (DistanceToTile(
                cameraPosition.X,
                cameraPosition.Z,
                key.X,
                key.Z) > LoadRadius)
        {
            return;
        }

        if (_loaded.ContainsKey(key))
        {
            return;
        }

        var resident = new ResidentChunk(chunk);

        try
        {
            foreach (VegetationBatch batch in chunk.Batches)
            {
                VegetationGpuModel? model =
                    _models[batch.ModelIndex];

                if (model is null ||
                    batch.Instances.Length == 0)
                {
                    continue;
                }

                resident.Batches.Add(
                    new VegetationGpuBatch(
                        _device,
                        model,
                        batch.Instances));
            }

            _loaded.Add(key, resident);
        }
        catch
        {
            resident.Dispose();
            throw;
        }
    }

    private (int X, int Z)? FindNearestMissing(
        Vector3 cameraPosition)
    {
        int centerX = GetTile(cameraPosition.X);
        int centerZ = GetTile(cameraPosition.Z);

        int tileRadius = (int)MathF.Ceiling(
            LoadRadius / VegetationChunk.TileSize) + 1;

        (int X, int Z)? nearest = null;
        float nearestDistance = float.PositiveInfinity;

        for (int dz = -tileRadius;
             dz <= tileRadius;
             dz++)
        {
            for (int dx = -tileRadius;
                 dx <= tileRadius;
                 dx++)
            {
                int tileX = centerX + dx;
                int tileZ = centerZ + dz;

                var key = (tileX, tileZ);

                if (_loaded.ContainsKey(key) ||
                    _rejected.Contains(key) ||
                    !_cells.ContainsKey(key))
                {
                    continue;
                }

                float distance = DistanceToTile(
                    cameraPosition.X,
                    cameraPosition.Z,
                    tileX,
                    tileZ);

                if (distance > LoadRadius ||
                    distance >= nearestDistance)
                {
                    continue;
                }

                nearest = key;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void UnloadDistant(Vector3 cameraPosition)
    {
        _unloadKeys.Clear();

        foreach (var pair in _loaded)
        {
            float distance = DistanceToTile(
                cameraPosition.X,
                cameraPosition.Z,
                pair.Key.X,
                pair.Key.Z);

            if (distance > UnloadRadius)
            {
                _unloadKeys.Add(pair.Key);
            }
        }

        foreach ((int X, int Z) key in _unloadKeys)
        {
            _loaded[key].Dispose();
            _loaded.Remove(key);
        }

        _unloadKeys.Clear();
    }

    private void IndexCells()
    {
        foreach (DetailCell cell in _details.Cells)
        {
            if (!float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y) ||
                !HasVegetation(cell))
            {
                continue;
            }

            int x = GetTile(cell.Position.X);
            int z = GetTile(cell.Position.Y);

            var key = (x, z);

            if (!_cells.TryGetValue(
                    key,
                    out List<DetailCell>? list))
            {
                list = new List<DetailCell>();
                _cells.Add(key, list);
            }

            list.Add(cell);
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

    private static int GetTile(float position)
    {
        return (int)MathF.Floor(
            position / VegetationChunk.TileSize);
    }

    private static float DistanceToTile(
        float worldX,
        float worldZ,
        int tileX,
        int tileZ)
    {
        float minimumX = tileX * VegetationChunk.TileSize;
        float minimumZ = tileZ * VegetationChunk.TileSize;

        float maximumX = minimumX + VegetationChunk.TileSize;
        float maximumZ = minimumZ + VegetationChunk.TileSize;

        float nearestX = Math.Clamp(
            worldX,
            minimumX,
            maximumX);

        float nearestZ = Math.Clamp(
            worldZ,
            minimumZ,
            maximumZ);

        float dx = worldX - nearestX;
        float dz = worldZ - nearestZ;

        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (ResidentChunk chunk in _loaded.Values)
        {
            chunk.Dispose();
        }

        _loaded.Clear();

        foreach (VegetationGpuModel? model in _models)
        {
            model?.Dispose();
        }

        _cells.Clear();
        _rejected.Clear();
        _unloadKeys.Clear();
    }

    private sealed class ResidentChunk : IDisposable
    {
        public VegetationChunk Data { get; }

        public List<VegetationGpuBatch> Batches { get; } =
            new();

        public ResidentChunk(VegetationChunk data)
        {
            Data = data;
        }

        public void Dispose()
        {
            foreach (VegetationGpuBatch batch in Batches)
            {
                batch.Dispose();
            }

            Batches.Clear();
        }
    }
}
