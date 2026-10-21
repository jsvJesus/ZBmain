
using System.Buffers.Binary;

namespace Core.Levels.XRay;

internal readonly record struct XRayChunk(
    uint Id,
    int Offset,
    int Size);

internal static class XRayChunkReader
{
    public static IEnumerable<XRayChunk> Enumerate(
        byte[] bytes,
        int offset,
        int length)
    {
        long end = (long)offset + length;

        if (offset < 0 || length < 0 || end > bytes.Length)
        {
            throw new InvalidDataException(
                "Повреждён диапазон X-Ray chunks.");
        }

        while (offset < end)
        {
            if (end - offset < 8)
            {
                throw new InvalidDataException(
                    "Неполный заголовок X-Ray chunk.");
            }

            uint id = UInt32(bytes, offset);
            uint rawSize = UInt32(bytes, offset + 4);
            int size = checked((int)(rawSize & 0x7fff_ffff));

            offset += 8;

            if ((id & 0x8000_0000) != 0 ||
                (long)offset + size > end)
            {
                throw new InvalidDataException(
                    "Сжатый или повреждённый X-Ray chunk.");
            }

            yield return new XRayChunk(id, offset, size);
            offset += size;
        }
    }

    public static XRayChunk? Find(
        byte[] bytes,
        int offset,
        int length,
        uint id)
    {
        foreach (XRayChunk chunk in Enumerate(
                     bytes,
                     offset,
                     length))
        {
            if (chunk.Id == id)
            {
                return chunk;
            }
        }

        return null;
    }

    public static XRayChunk Required(
        byte[] bytes,
        int offset,
        int length,
        uint id)
    {
        return Find(bytes, offset, length, id) ??
               throw new InvalidDataException(
                   $"Не найден X-Ray chunk 0x{id:X}.");
    }

    public static uint UInt32(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(offset, 4));
    }

    public static ushort UInt16(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(offset, 2));
    }

    public static float Single(byte[] bytes, int offset)
    {
        return BitConverter.ToSingle(bytes, offset);
    }
}
