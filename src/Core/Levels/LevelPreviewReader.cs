using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Core.Levels;

public readonly record struct PreviewTriangle(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    ushort Material);

public sealed record LevelPreview(
    string Name,
    IReadOnlyList<PreviewTriangle> Triangles,
    Vector3 Min,
    Vector3 Max,
    uint SourceVertexCount,
    uint SourceTriangleCount,
    bool HasVisualGeometry,
    int VisualMeshes,
    long VisualTriangles,
    int Materials);

public sealed class LevelPreviewReader
{
    public LevelPreview Read(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);

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

        ZipArchiveEntry? visualEntry =
            archive.GetEntry("geometry/preview.zmesh");

        if (visualEntry is not null)
        {
            using Stream stream = visualEntry.Open();

            using var reader = new BinaryReader(
                stream,
                Encoding.UTF8);

            if (!reader.ReadBytes(4).SequenceEqual(
                    "ZPRV"u8.ToArray()) ||
                reader.ReadInt32() != 1)
            {
                throw new InvalidDataException(
                    "Неизвестная версия preview.zmesh.");
            }

            int count = reader.ReadInt32();

            if (count < 0 || count > 200_000)
            {
                throw new InvalidDataException(
                    "Повреждено количество граней preview.zmesh.");
            }

            Vector3 min = ReadVector3(reader);
            Vector3 max = ReadVector3(reader);

            var faces = new PreviewTriangle[count];

            for (int i = 0; i < count; i++)
            {
                faces[i] = new PreviewTriangle(
                    ReadVector3(reader),
                    ReadVector3(reader),
                    ReadVector3(reader),
                    reader.ReadUInt16());
            }

            return new LevelPreview(
                manifest.Name,
                faces,
                min,
                max,
                manifest.CollisionVertices,
                manifest.CollisionTriangles,
                true,
                manifest.VisualMeshes,
                manifest.VisualTriangles,
                manifest.Materials);
        }

        return ReadLegacyCollision(
            archive,
            manifest);
    }

    private static LevelPreview ReadLegacyCollision(
        ZipArchive archive,
        LevelManifest manifest)
    {
        ZipArchiveEntry? mesh =
            archive.GetEntry("geometry/collision.zmesh");

        if (mesh is null)
        {
            return new LevelPreview(
                manifest.Name,
                [],
                new Vector3(-50),
                new Vector3(50),
                0,
                0,
                false,
                0,
                0,
                0);
        }

        using Stream stream = mesh.Open();

        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8);

        if (!reader.ReadBytes(4).SequenceEqual(
                "ZMSH"u8.ToArray()) ||
            reader.ReadUInt32() != 1)
        {
            throw new InvalidDataException(
                "Неизвестная версия collision.zmesh.");
        }

        uint vertexCount = reader.ReadUInt32();
        uint triangleCount = reader.ReadUInt32();

        Vector3 min = ReadVector3(reader);
        Vector3 max = ReadVector3(reader);

        if (vertexCount > 20_000_000 ||
            triangleCount > 30_000_000)
        {
            throw new InvalidDataException(
                "Недопустимый размер коллизии.");
        }

        var vertices = new Vector3[checked((int)vertexCount)];

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = ReadVector3(reader);
        }

        int stride = Math.Max(
            1,
            (int)(triangleCount / 30_000));

        var faces = new List<PreviewTriangle>();

        for (uint i = 0; i < triangleCount; i++)
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
                faces.Add(new PreviewTriangle(
                    vertices[a],
                    vertices[b],
                    vertices[c],
                    0));
            }
        }

        return new LevelPreview(
            manifest.Name,
            faces,
            min,
            max,
            vertexCount,
            triangleCount,
            false,
            0,
            0,
            0);
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
    }
}
