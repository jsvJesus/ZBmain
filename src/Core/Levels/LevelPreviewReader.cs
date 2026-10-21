
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Core.Levels;

public readonly record struct PreviewTriangle(
    Vector2 A,
    Vector2 B,
    Vector2 C);

public sealed record LevelPreview(
    string Name,
    IReadOnlyList<PreviewTriangle> Triangles,
    Vector2 Min,
    Vector2 Max,
    uint SourceVertexCount,
    uint SourceTriangleCount);

public sealed class LevelPreviewReader
{
    public LevelPreview Read(string packagePath)
    {
        using ZipArchive archive =
            ZipFile.OpenRead(packagePath);

        ZipArchiveEntry manifestEntry =
            archive.GetEntry("scene.json") ??
            throw new InvalidDataException(
                "Отсутствует scene.json.");

        LevelManifest manifest;

        using (Stream json = manifestEntry.Open())
        {
            manifest =
                JsonSerializer.Deserialize<LevelManifest>(json) ??
                throw new InvalidDataException(
                    "Повреждён scene.json.");
        }

        ZipArchiveEntry? mesh =
            archive.GetEntry("geometry/collision.zmesh");

        if (mesh is null)
        {
            return new LevelPreview(
                manifest.Name,
                Array.Empty<PreviewTriangle>(),
                new Vector2(-50),
                new Vector2(50),
                0,
                0);
        }

        using Stream stream = mesh.Open();

        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8,
            leaveOpen: false);

        if (!reader.ReadBytes(4).SequenceEqual(
                "ZMSH"u8.ToArray()) ||
            reader.ReadUInt32() != 1)
        {
            throw new InvalidDataException(
                "Неизвестная версия collision.zmesh.");
        }

        uint vertexCount = reader.ReadUInt32();
        uint triangleCount = reader.ReadUInt32();

        float[] box = new float[6];

        for (int i = 0; i < box.Length; i++)
        {
            box[i] = reader.ReadSingle();
        }

        if (vertexCount > 20_000_000 ||
            triangleCount > 30_000_000)
        {
            throw new InvalidDataException(
                "Геометрия превышает допустимые размеры.");
        }

        var vertices =
            new Vector2[checked((int)vertexCount)];

        for (int i = 0; i < vertices.Length; i++)
        {
            float x = reader.ReadSingle();

            reader.ReadSingle();

            float z = reader.ReadSingle();

            vertices[i] = new Vector2(x, z);
        }

        long stride = Math.Max(
            1,
            (long)triangleCount / 3_500);

        var preview = new List<PreviewTriangle>();

        for (long i = 0; i < triangleCount; i++)
        {
            uint a = reader.ReadUInt32();
            uint b = reader.ReadUInt32();
            uint c = reader.ReadUInt32();

            reader.ReadUInt32();

            if (i % stride == 0 &&
                a < vertexCount &&
                b < vertexCount &&
                c < vertexCount)
            {
                preview.Add(new PreviewTriangle(
                    vertices[a],
                    vertices[b],
                    vertices[c]));
            }
        }

        return new LevelPreview(
            manifest.Name,
            preview,
            new Vector2(box[0], box[2]),
            new Vector2(box[3], box[5]),
            vertexCount,
            triangleCount);
    }
}
