using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Core.Levels;

public sealed record RenderVertexBuffer(float[] Data)
{
    public const int FloatsPerVertex = 7;

    public int Count => Data.Length / FloatsPerVertex;
}

public sealed record RenderIndexBuffer(ushort[] Data);

public sealed record RenderSurface(
    int Material,
    int Type,
    int VertexBuffer,
    int VertexOffset,
    int VertexCount,
    int IndexBuffer,
    int IndexOffset,
    int IndexCount,
    Matrix4x4 Transform);

public sealed record RenderLevel(
    string Name,
    string[] Materials,
    RenderVertexBuffer[] Vertices,
    RenderIndexBuffer[] Indices,
    RenderSurface[] Surfaces,
    Vector3 Min,
    Vector3 Max);

public sealed class RenderLevelReader
{
    public RenderLevel Read(string path)
    {
        if (Directory.Exists(path))
        {
            return ReadDirectory(path);
        }

        if (File.Exists(path))
        {
            return ReadArchive(path);
        }

        throw new FileNotFoundException(
            $"Уровень не найден: {path}");
    }

    private static RenderLevel ReadDirectory(string directory)
    {
        string geometryPath = Path.Combine(
            directory,
            "geometry",
            "render.zgeom");

        if (!File.Exists(geometryPath))
        {
            throw new InvalidDataException(
                "В папке уровня отсутствует geometry/render.zgeom.");
        }

        string collisionPath = Path.Combine(
            directory,
            "geometry",
            "collision.zmesh");

        using Stream geometry = File.OpenRead(geometryPath);

        return ReadGeometry(
            geometry,
            Path.GetFileName(directory),
            () =>
            {
                if (!File.Exists(collisionPath))
                {
                    return DefaultBounds();
                }

                using Stream collision = File.OpenRead(
                    collisionPath);

                return ReadBounds(collision);
            });
    }

    private static RenderLevel ReadArchive(string packagePath)
    {
        using ZipArchive archive =
            ZipFile.OpenRead(packagePath);

        ZipArchiveEntry geometryEntry =
            archive.GetEntry("geometry/render.zgeom") ??
            throw new InvalidDataException(
                "В архиве отсутствует geometry/render.zgeom.");

        using Stream geometry = geometryEntry.Open();

        return ReadGeometry(
            geometry,
            Path.GetFileNameWithoutExtension(packagePath),
            () =>
            {
                ZipArchiveEntry? collisionEntry =
                    archive.GetEntry("geometry/collision.zmesh");

                if (collisionEntry is null)
                {
                    return DefaultBounds();
                }

                using Stream collision = collisionEntry.Open();

                return ReadBounds(collision);
            });
    }

    private static RenderLevel ReadGeometry(
        Stream stream,
        string levelName,
        Func<(Vector3 Min, Vector3 Max)> readBounds)
    {
        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8,
            leaveOpen: true);

        if (!reader.ReadBytes(4).SequenceEqual("ZRGM"u8))
        {
            throw new InvalidDataException(
                "Неверная сигнатура render.zgeom.");
        }

        int version = reader.ReadInt32();

        if (version is not (1 or 2 or 3))
        {
            throw new InvalidDataException(
                $"Неподдерживаемая версия геометрии: {version}.");
        }

        int materialCount = ReadCount(reader, 100_000);
        int vertexBufferCount = ReadCount(reader, 512);
        int indexBufferCount = ReadCount(reader, 512);
        int surfaceCount = ReadCount(reader, 200_000);

        var materials = new string[materialCount];
        var vertexBuffers =
            new RenderVertexBuffer[vertexBufferCount];

        var indexBuffers =
            new RenderIndexBuffer[indexBufferCount];

        var surfaces = new RenderSurface[surfaceCount];

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = reader.ReadString();
        }

        for (int i = 0; i < vertexBuffers.Length; i++)
        {
            int vertexCount = ReadCount(
                reader,
                20_000_000);

            var data = new float[
                checked(
                    vertexCount *
                    RenderVertexBuffer.FloatsPerVertex)];

            for (int vertex = 0;
                 vertex < vertexCount;
                 vertex++)
            {
                int offset =
                    vertex * RenderVertexBuffer.FloatsPerVertex;

                data[offset] = reader.ReadSingle();
                data[offset + 1] = reader.ReadSingle();
                data[offset + 2] = reader.ReadSingle();

                data[offset + 3] = reader.ReadSingle();
                data[offset + 4] = reader.ReadSingle();

                if (version >= 2)
                {
                    data[offset + 5] = reader.ReadSingle();
                    data[offset + 6] = reader.ReadSingle();
                }
                else
                {
                    data[offset + 5] = 0f;
                    data[offset + 6] = 0f;
                }
            }

            vertexBuffers[i] = new RenderVertexBuffer(data);
        }

        for (int i = 0; i < indexBuffers.Length; i++)
        {
            int count = ReadCount(reader, 60_000_000);
            var data = new ushort[count];

            for (int j = 0; j < count; j++)
            {
                data[j] = reader.ReadUInt16();
            }

            indexBuffers[i] = new RenderIndexBuffer(data);
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            int material = reader.ReadInt32();
            int type = reader.ReadInt32();

            int vb = reader.ReadInt32();
            int vertexOffset = reader.ReadInt32();
            int vertexCount = reader.ReadInt32();

            int ib = reader.ReadInt32();
            int indexOffset = reader.ReadInt32();
            int indexCount = reader.ReadInt32();

            Matrix4x4 transform = ReadMatrix(reader);

            if (vb < 0 || vb >= vertexBuffers.Length ||
                ib < 0 || ib >= indexBuffers.Length ||
                vertexOffset < 0 ||
                vertexCount < 0 ||
                indexOffset < 0 ||
                indexCount < 0 ||
                (long)vertexOffset + vertexCount >
                    vertexBuffers[vb].Count ||
                (long)indexOffset + indexCount >
                    indexBuffers[ib].Data.Length)
            {
                throw new InvalidDataException(
                    $"Повреждена ссылка на геометрию меша {i}.");
            }

            surfaces[i] = new RenderSurface(
                material,
                type,
                vb,
                vertexOffset,
                vertexCount,
                ib,
                indexOffset,
                indexCount,
                transform);
        }

        (Vector3 min, Vector3 max) = readBounds();

        if (version < 3)
        {
            ApplyLegacyMirrorX(vertexBuffers, surfaces);

            (min, max) = MirrorBoundsX(min, max);
        }

        if (min == max)
        {
            (min, max) = DefaultBounds();
        }

        return new RenderLevel(
            levelName,
            materials,
            vertexBuffers,
            indexBuffers,
            surfaces,
            min,
            max);
    }

    private static void ApplyLegacyMirrorX(
        RenderVertexBuffer[] vertexBuffers,
        RenderSurface[] surfaces)
    {
        foreach (RenderVertexBuffer buffer in vertexBuffers)
        {
            for (int i = 0;
                 i < buffer.Data.Length;
                 i += RenderVertexBuffer.FloatsPerVertex)
            {
                buffer.Data[i] = -buffer.Data[i];
            }
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            RenderSurface surface = surfaces[i];

            surfaces[i] = surface with
            {
                Transform = MirrorTransformX(
                    surface.Transform)
            };
        }
    }

    private static Matrix4x4 MirrorTransformX(
        Matrix4x4 source)
    {
        Matrix4x4 mirror = new(
            -1f, 0f, 0f, 0f,
             0f, 1f, 0f, 0f,
             0f, 0f, 1f, 0f,
             0f, 0f, 0f, 1f);

        return mirror * source * mirror;
    }

    private static (Vector3 Min, Vector3 Max) ReadBounds(
        Stream stream)
    {
        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8,
            leaveOpen: true);

        if (!reader.ReadBytes(4).SequenceEqual("ZMSH"u8) ||
            reader.ReadUInt32() != 1)
        {
            return DefaultBounds();
        }

        reader.ReadUInt32();
        reader.ReadUInt32();

        Vector3 min = new(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

        Vector3 max = new(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

        return (min, max);
    }

    private static (Vector3 Min, Vector3 Max) DefaultBounds()
    {
        return (
            new Vector3(-500f),
            new Vector3(500f));
    }

    private static (Vector3 Min, Vector3 Max) MirrorBoundsX(
        Vector3 min,
        Vector3 max)
    {
        return (
            new Vector3(-max.X, min.Y, min.Z),
            new Vector3(-min.X, max.Y, max.Z));
    }

    private static int ReadCount(
        BinaryReader reader,
        int maximum)
    {
        int count = reader.ReadInt32();

        if (count < 0 || count > maximum)
        {
            throw new InvalidDataException(
                $"Недопустимое количество элементов: {count}.");
        }

        return count;
    }

    private static Matrix4x4 ReadMatrix(
        BinaryReader reader)
    {
        return new Matrix4x4(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),

            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),

            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),

            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
    }
}
