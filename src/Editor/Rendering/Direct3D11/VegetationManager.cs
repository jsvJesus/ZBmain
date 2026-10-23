using System.Numerics;
using Core.Levels;
using Vortice.Direct3D11;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationManager : IDisposable
{
    private const float LoadRadius = 128f;
    private const float UnloadRadius = 176f;
    private const int ChunksPerFrame = 2;

    private readonly ID3D11Device _device;
    private readonly DetailLevel _details;
    private readonly TerrainHeightSampler? _terrain;

    private readonly Dictionary<
        (int X, int Z),
        List<DetailCell>> _cells = new();

    private readonly Dictionary<
        (int X, int Z),
        ResidentChunk> _loaded = new();

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

        IndexCells();
    }

    public void Update(Vector3 cameraPosition)
    {
        float x = cameraPosition.X;
        float z = cameraPosition.Z;

        foreach (var pair in _loaded.ToArray())
        {
            float distance = DistanceToTile(
                x,
                z,
                pair.Key.X,
                pair.Key.Z);

            if (distance <= UnloadRadius)
            {
                continue;
            }

            pair.Value.Dispose();
            _loaded.Remove(pair.Key);
        }

        int centerX = GetTile(x);
        int centerZ = GetTile(z);

        int tileRadius =
            (int)MathF.Ceiling(
                LoadRadius / VegetationChunk.TileSize) + 1;

        var candidates = new List<(
            int X,
            int Z,
            float Distance)>();

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
                    !_cells.ContainsKey(key))
                {
                    continue;
                }

                float distance = DistanceToTile(
                    x,
                    z,
                    tileX,
                    tileZ);

                if (distance > LoadRadius)
                {
                    continue;
                }

                candidates.Add((
                    tileX,
                    tileZ,
                    distance));
            }
        }

        candidates.Sort(
            (a, b) => a.Distance.CompareTo(b.Distance));

        int created = 0;

        foreach (var candidate in candidates)
        {
            if (created >= ChunksPerFrame)
            {
                break;
            }

            var key = (
                candidate.X,
                candidate.Z);

            VegetationChunk chunk =
                VegetationChunk.Build(
                    key.Item1,
                    key.Item2,
                    _cells[key],
                    _details,
                    _terrain);

            var resident = new ResidentChunk(chunk);

            try
            {
                foreach (VegetationBatch batch in chunk.Batches)
                {
                    DetailModel model =
                        _details.Models[batch.ModelIndex];

                    resident.Meshes.Add(
                        new VegetationGpuMesh(
                            _device,
                            model,
                            batch.Instances));
                }

                _loaded.Add(key, resident);
                created++;
            }
            catch
            {
                resident.Dispose();
                throw;
            }
        }
    }

    public void Draw(
        ID3D11DeviceContext context,
        VegetationTextureCache textures,
        Matrix4x4 viewProjection)
    {
        foreach (ResidentChunk resident in _loaded.Values)
        {
            if (!IsVisible(
                    viewProjection,
                    resident.Data.Center,
                    resident.Data.Radius))
            {
                continue;
            }

            foreach (VegetationGpuMesh mesh in resident.Meshes)
            {
                context.PSSetShaderResource(
                    0,
                    textures.Get(mesh.TextureName));

                mesh.Draw(context);
            }
        }
    }

    private void IndexCells()
    {
        foreach (DetailCell cell in _details.Cells)
        {
            if (!float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y))
            {
                continue;
            }

            if (!HasVegetation(cell))
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
        float minimumX =
            tileX * VegetationChunk.TileSize;

        float minimumZ =
            tileZ * VegetationChunk.TileSize;

        float maximumX =
            minimumX + VegetationChunk.TileSize;

        float maximumZ =
            minimumZ + VegetationChunk.TileSize;

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

    private static bool IsVisible(
        Matrix4x4 matrix,
        Vector3 center,
        float radius)
    {
        Vector4[] planes =
        [
            new(
                matrix.M14 + matrix.M11,
                matrix.M24 + matrix.M21,
                matrix.M34 + matrix.M31,
                matrix.M44 + matrix.M41),

            new(
                matrix.M14 - matrix.M11,
                matrix.M24 - matrix.M21,
                matrix.M34 - matrix.M31,
                matrix.M44 - matrix.M41),

            new(
                matrix.M14 + matrix.M12,
                matrix.M24 + matrix.M22,
                matrix.M34 + matrix.M32,
                matrix.M44 + matrix.M42),

            new(
                matrix.M14 - matrix.M12,
                matrix.M24 - matrix.M22,
                matrix.M34 - matrix.M32,
                matrix.M44 - matrix.M42),

            new(
                matrix.M13,
                matrix.M23,
                matrix.M33,
                matrix.M43),

            new(
                matrix.M14 - matrix.M13,
                matrix.M24 - matrix.M23,
                matrix.M34 - matrix.M33,
                matrix.M44 - matrix.M43)
        ];

        foreach (Vector4 plane in planes)
        {
            float length = MathF.Sqrt(
                plane.X * plane.X +
                plane.Y * plane.Y +
                plane.Z * plane.Z);

            if (length <= 0.000001f)
            {
                continue;
            }

            float distance = (
                plane.X * center.X +
                plane.Y * center.Y +
                plane.Z * center.Z +
                plane.W) / length;

            if (distance < -radius)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        foreach (ResidentChunk resident in _loaded.Values)
        {
            resident.Dispose();
        }

        _loaded.Clear();
        _cells.Clear();
    }

    private sealed class ResidentChunk : IDisposable
    {
        public VegetationChunk Data { get; }

        public List<VegetationGpuMesh> Meshes { get; } =
            new();

        public ResidentChunk(VegetationChunk data)
        {
            Data = data;
        }

        public void Dispose()
        {
            foreach (VegetationGpuMesh mesh in Meshes)
            {
                mesh.Dispose();
            }

            Meshes.Clear();
        }
    }
}
