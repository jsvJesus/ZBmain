using System.IO.Compression;
using System.Numerics;
using Core.Levels;

namespace Editor.Rendering.Direct3D11;

internal sealed class TerrainHeightSampler
{
    private const int MaximumVertices = 20_000_000;
    private const int MaximumTriangles = 30_000_000;
    private const float MinimumUpComponent = 0.20f;
    private const float HeightTolerance = 2f;

    private readonly Vector3[] _vertices;
    private readonly List<Triangle> _triangles = new();

    private readonly Dictionary<
        (int X, int Z),
        List<int>> _tiles = new();

    private TerrainHeightSampler(Vector3[] vertices)
    {
        _vertices = vertices;
    }

    public static TerrainHeightSampler? Load(
        string levelName,
        DetailLevel details)
    {
        string levelPath = LevelPaths.OpenPath(levelName);

        if (Directory.Exists(levelPath))
        {
            string path = Path.Combine(
                levelPath,
                "geometry",
                "collision.zmesh");

            if (!File.Exists(path))
            {
                return null;
            }

            using Stream stream = File.OpenRead(path);

            return Read(stream, details);
        }

        using ZipArchive archive = ZipFile.OpenRead(levelPath);

        ZipArchiveEntry? entry = archive.GetEntry(
            "geometry/collision.zmesh");

        if (entry is null)
        {
            return null;
        }

        using Stream archiveStream = entry.Open();

        return Read(archiveStream, details);
    }

    public bool TryGetHeight(
        float x,
        float z,
        float baseHeight,
        float topHeight,
        out float height)
    {
        height = 0f;

        if (!float.IsFinite(x) ||
            !float.IsFinite(z) ||
            !float.IsFinite(baseHeight) ||
            !float.IsFinite(topHeight))
        {
            return false;
        }

        int tileX = GetTile(x);
        int tileZ = GetTile(z);

        if (!_tiles.TryGetValue(
                (tileX, tileZ),
                out List<int>? triangles))
        {
            return false;
        }

        float minimumHeight = MathF.Min(
            baseHeight,
            topHeight) - HeightTolerance;

        float maximumHeight = MathF.Max(
            baseHeight,
            topHeight) + HeightTolerance;

        bool found = false;
        float bestHeight = float.NegativeInfinity;

        foreach (int index in triangles)
        {
            Triangle triangle = _triangles[index];

            Vector3 a = _vertices[triangle.A];
            Vector3 b = _vertices[triangle.B];
            Vector3 c = _vertices[triangle.C];

            if (!TryInterpolateHeight(
                    a,
                    b,
                    c,
                    x,
                    z,
                    out float surfaceHeight))
            {
                continue;
            }

            if (surfaceHeight < minimumHeight ||
                surfaceHeight > maximumHeight)
            {
                continue;
            }

            if (!found || surfaceHeight > bestHeight)
            {
                bestHeight = surfaceHeight;
                found = true;
            }
        }

        if (found)
        {
            height = bestHeight;
        }

        return found;
    }

    private static TerrainHeightSampler Read(
        Stream source,
        DetailLevel details)
    {
        using var reader = new BinaryReader(
            source,
            System.Text.Encoding.UTF8,
            leaveOpen: true);

        if (!reader.ReadBytes(4).SequenceEqual("ZMSH"u8))
        {
            throw new InvalidDataException(
                "Неверная сигнатура collision.zmesh.");
        }

        uint version = reader.ReadUInt32();
        uint vertexCount = reader.ReadUInt32();
        uint triangleCount = reader.ReadUInt32();

        if (version != 1 ||
            vertexCount == 0 ||
            vertexCount > MaximumVertices ||
            triangleCount > MaximumTriangles)
        {
            throw new InvalidDataException(
                "Неподдерживаемый collision.zmesh.");
        }

        for (int i = 0; i < 6; i++)
        {
            float bound = reader.ReadSingle();

            if (!float.IsFinite(bound))
            {
                throw new InvalidDataException(
                    "Некорректные границы collision.zmesh.");
            }
        }

        var vertices = new Vector3[(int)vertexCount];

        for (int i = 0; i < vertices.Length; i++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();

            vertices[i] = new Vector3(
                -x,
                y,
                z);
        }

        var sampler = new TerrainHeightSampler(vertices);

        HashSet<(int X, int Z)> relevantTiles =
            FindVegetationTiles(details);

        for (uint i = 0; i < triangleCount; i++)
        {
            uint ia = reader.ReadUInt32();
            uint ib = reader.ReadUInt32();
            uint ic = reader.ReadUInt32();

            reader.ReadUInt32();

            if (ia >= vertexCount ||
                ib >= vertexCount ||
                ic >= vertexCount)
            {
                throw new InvalidDataException(
                    "Некорректный индекс collision.zmesh.");
            }

            Vector3 a = vertices[(int)ia];
            Vector3 b = vertices[(int)ib];
            Vector3 c = vertices[(int)ic];

            Vector3 normal = Vector3.Cross(
                b - a,
                c - a);

            float length = normal.Length();

            if (length <= 0.00001f ||
                MathF.Abs(normal.Y) / length <
                MinimumUpComponent)
            {
                continue;
            }

            float minimumX = MathF.Min(
                a.X,
                MathF.Min(b.X, c.X));

            float maximumX = MathF.Max(
                a.X,
                MathF.Max(b.X, c.X));

            float minimumZ = MathF.Min(
                a.Z,
                MathF.Min(b.Z, c.Z));

            float maximumZ = MathF.Max(
                a.Z,
                MathF.Max(b.Z, c.Z));

            int startX = GetTile(minimumX);
            int endX = GetTile(maximumX);
            int startZ = GetTile(minimumZ);
            int endZ = GetTile(maximumZ);

            long tileCount =
                (long)(endX - startX + 1) *
                (endZ - startZ + 1);

            if (tileCount > 2048)
            {
                continue;
            }

            int triangleIndex = -1;

            for (int z = startZ; z <= endZ; z++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    var key = (x, z);

                    if (!relevantTiles.Contains(key))
                    {
                        continue;
                    }

                    if (triangleIndex < 0)
                    {
                        triangleIndex =
                            sampler._triangles.Count;

                        sampler._triangles.Add(
                            new Triangle(
                                (int)ia,
                                (int)ib,
                                (int)ic));
                    }

                    if (!sampler._tiles.TryGetValue(
                            key,
                            out List<int>? indices))
                    {
                        indices = new List<int>();
                        sampler._tiles.Add(key, indices);
                    }

                    indices.Add(triangleIndex);
                }
            }
        }

        return sampler;
    }

    private static HashSet<(int X, int Z)>
        FindVegetationTiles(DetailLevel details)
    {
        var result = new HashSet<(int X, int Z)>();

        foreach (DetailCell cell in details.Cells)
        {
            if (!float.IsFinite(cell.Position.X) ||
                !float.IsFinite(cell.Position.Y))
            {
                continue;
            }

            if (!IsActive(cell.Layer0) &&
                !IsActive(cell.Layer1) &&
                !IsActive(cell.Layer2) &&
                !IsActive(cell.Layer3))
            {
                continue;
            }

            int x = GetTile(cell.Position.X);
            int z = GetTile(cell.Position.Y);

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    result.Add((x + dx, z + dz));
                }
            }
        }

        return result;
    }

    private static bool IsActive(DetailLayer layer)
    {
        return layer.Model != byte.MaxValue &&
               layer.Density != 0;
    }

    private static int GetTile(float value)
    {
        return (int)MathF.Floor(
            value / VegetationChunk.TileSize);
    }

    private static bool TryInterpolateHeight(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float x,
        float z,
        out float height)
    {
        height = 0f;

        float denominator =
            (b.Z - c.Z) * (a.X - c.X) +
            (c.X - b.X) * (a.Z - c.Z);

        if (MathF.Abs(denominator) < 0.000001f)
        {
            return false;
        }

        float wa = (
            (b.Z - c.Z) * (x - c.X) +
            (c.X - b.X) * (z - c.Z)) /
            denominator;

        float wb = (
            (c.Z - a.Z) * (x - c.X) +
            (a.X - c.X) * (z - c.Z)) /
            denominator;

        float wc = 1f - wa - wb;

        const float epsilon = 0.0001f;

        if (wa < -epsilon ||
            wb < -epsilon ||
            wc < -epsilon)
        {
            return false;
        }

        height =
            wa * a.Y +
            wb * b.Y +
            wc * c.Y;

        return float.IsFinite(height);
    }

    private readonly record struct Triangle(
        int A,
        int B,
        int C);
}
