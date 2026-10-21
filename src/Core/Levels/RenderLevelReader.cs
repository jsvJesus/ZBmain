
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Core.Levels;

public sealed record RenderVertexBuffer(float[] Data)
{
    public int Count => Data.Length / 5;
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
    public RenderLevel Read(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);

        ZipArchiveEntry entry =
            archive.GetEntry("geometry/render.zgeom") ??
            throw new InvalidDataException(
                "В уровне нет render.zgeom. Выполните повторный импорт.");

        using Stream stream = entry.Open();
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (!reader.ReadBytes(4).SequenceEqual("ZRGM"u8) ||
            reader.ReadInt32() != 1)
        {
            throw new InvalidDataException(
                "Неподдерживаемая версия render.zgeom.");
        }

        int materialCount = ReadCount(reader, 100_000);
        int vertexBufferCount = ReadCount(reader, 512);
        int indexBufferCount = ReadCount(reader, 512);
        int surfaceCount = ReadCount(reader, 200_000);

        var materials = new string[materialCount];
        var vertexBuffers = new RenderVertexBuffer[vertexBufferCount];
        var indexBuffers = new RenderIndexBuffer[indexBufferCount];
        var surfaces = new RenderSurface[surfaceCount];

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = reader.ReadString();
        }

        for (int i = 0; i < vertexBuffers.Length; i++)
        {
            int vertexCount = ReadCount(reader, 20_000_000);
            var data = new float[checked(vertexCount * 5)];

            for (int j = 0; j < data.Length; j++)
            {
                data[j] = reader.ReadSingle();
            }

            vertexBuffers[i] = new RenderVertexBuffer(data);
        }

        for (int i = 0; i < indexBuffers.Length; i++)
        {
            int indexCount = ReadCount(reader, 60_000_000);
            var data = new ushort[indexCount];

            for (int j = 0; j < data.Length; j++)
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
                    $"Повреждена ссылка на геометрию объекта {i}.");
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

        (Vector3 min, Vector3 max) = ReadBounds(archive);

        if (min == max)
        {
            min = new Vector3(-500);
            max = new Vector3(500);
        }

        return new RenderLevel(
            Path.GetFileNameWithoutExtension(packagePath),
            materials,
            vertexBuffers,
            indexBuffers,
            surfaces,
            min,
            max);
    }

    private static int ReadCount(BinaryReader reader, int max)
    {
        int count = reader.ReadInt32();

        if (count < 0 || count > max)
        {
            throw new InvalidDataException(
                $"Некорректное количество элементов: {count}.");
        }

        return count;
    }

    private static Matrix4x4 ReadMatrix(BinaryReader reader)
    {
        return new Matrix4x4(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());
    }

    private static (Vector3 Min, Vector3 Max) ReadBounds(
        ZipArchive archive)
    {
        ZipArchiveEntry? collision =
            archive.GetEntry("geometry/collision.zmesh");

        if (collision is null)
        {
            return (new Vector3(-500), new Vector3(500));
        }

        using Stream stream = collision.Open();
        using var reader = new BinaryReader(stream);

        if (!reader.ReadBytes(4).SequenceEqual("ZMSH"u8) ||
            reader.ReadUInt32() != 1)
        {
            return (new Vector3(-500), new Vector3(500));
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
}
