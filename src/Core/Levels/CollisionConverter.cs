
using System.Text;

namespace Core.Levels;

public static class CollisionConverter
{
    private static readonly byte[] Signature =
        "ZMSH"u8.ToArray();

    public static (uint Vertices, uint Triangles) Convert(
        Stream source,
        Stream destination)
    {
        using var reader = new BinaryReader(
            source,
            Encoding.UTF8,
            leaveOpen: true);

        using var writer = new BinaryWriter(
            destination,
            Encoding.UTF8,
            leaveOpen: true);

        if (!source.CanSeek || source.Length < 36)
        {
            throw new InvalidDataException(
                "level.cform: отсутствует заголовок.");
        }

        uint sourceVersion = reader.ReadUInt32();
        uint vertexCount = reader.ReadUInt32();
        uint triangleCount = reader.ReadUInt32();

        float[] bounds = new float[6];

        for (int i = 0; i < bounds.Length; i++)
        {
            bounds[i] = reader.ReadSingle();
        }

        long expectedBytes =
            36L +
            12L * vertexCount +
            16L * triangleCount;

        if (sourceVersion != 4 ||
            vertexCount == 0 ||
            triangleCount == 0 ||
            vertexCount > 20_000_000 ||
            triangleCount > 30_000_000 ||
            expectedBytes > source.Length ||
            bounds.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException(
                $"Неподдерживаемый level.cform: " +
                $"версия {sourceVersion}, " +
                $"вершин {vertexCount}, " +
                $"треугольников {triangleCount}.");
        }

        writer.Write(Signature);
        writer.Write(1u);
        writer.Write(vertexCount);
        writer.Write(triangleCount);

        foreach (float bound in bounds)
        {
            writer.Write(bound);
        }

        for (uint i = 0; i < vertexCount; i++)
        {
            writer.Write(reader.ReadSingle());
            writer.Write(reader.ReadSingle());
            writer.Write(reader.ReadSingle());
        }

        for (uint i = 0; i < triangleCount; i++)
        {
            uint a = reader.ReadUInt32();
            uint b = reader.ReadUInt32();
            uint c = reader.ReadUInt32();
            uint material = reader.ReadUInt32();

            if (a >= vertexCount ||
                b >= vertexCount ||
                c >= vertexCount)
            {
                throw new InvalidDataException(
                    $"level.cform: неверный индекс грани {i}.");
            }

            writer.Write(a);
            writer.Write(b);
            writer.Write(c);
            writer.Write(material);
        }

        return (vertexCount, triangleCount);
    }
}
