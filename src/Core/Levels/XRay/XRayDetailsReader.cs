
using System.Numerics;
using System.Text;

namespace Core.Levels.XRay;

public readonly record struct XRayDetailVertex(
    Vector3 Position,
    Vector2 Uv);

public sealed record XRayDetailModel(
    string Shader,
    string Texture,
    uint Flags,
    float MinScale,
    float MaxScale,
    XRayDetailVertex[] Vertices,
    ushort[] Indices);

public readonly record struct XRayDetailLayer(
    byte Model,
    ushort Density);

public readonly record struct XRayDetailSlot(
    float BaseHeight,
    float TopHeight,
    XRayDetailLayer Layer0,
    XRayDetailLayer Layer1,
    XRayDetailLayer Layer2,
    XRayDetailLayer Layer3);

public sealed record XRayDetails(
    int SourceVersion,
    int OffsetX,
    int OffsetZ,
    int SizeX,
    int SizeZ,
    XRayDetailModel[] Models,
    XRayDetailSlot[] Slots);

public sealed class XRayDetailsReader
{
    public XRayDetails Read(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);

        XRayChunk header = XRayChunkReader.Required(
            data,
            0,
            data.Length,
            0);

        XRayChunk meshes = XRayChunkReader.Required(
            data,
            0,
            data.Length,
            1);

        XRayChunk slots = XRayChunkReader.Required(
            data,
            0,
            data.Length,
            2);

        if (header.Size < 24)
        {
            throw new InvalidDataException(
                "Повреждён заголовок level.details.");
        }

        int sourceVersion = checked((int)XRayChunkReader.UInt32(
            data,
            header.Offset));

        if (sourceVersion is not (2 or 3))
        {
            throw new NotSupportedException(
                $"Неизвестная версия level.details: {sourceVersion}.");
        }

        int modelCount = checked((int)XRayChunkReader.UInt32(
            data,
            header.Offset + 4));

        int offsetX = unchecked((int)XRayChunkReader.UInt32(
            data,
            header.Offset + 8));

        int offsetZ = unchecked((int)XRayChunkReader.UInt32(
            data,
            header.Offset + 12));

        int sizeX = checked((int)XRayChunkReader.UInt32(
            data,
            header.Offset + 16));

        int sizeZ = checked((int)XRayChunkReader.UInt32(
            data,
            header.Offset + 20));

        if (modelCount < 0 || modelCount > 63 ||
            sizeX <= 0 || sizeZ <= 0)
        {
            throw new InvalidDataException(
                "Некорректные размеры detail-сетки.");
        }

        long slotCount = (long)sizeX * sizeZ;

        if (slotCount > 5_000_000)
        {
            throw new InvalidDataException(
                "Превышен размер detail-сетки.");
        }

        int sourceSlotSize = sourceVersion == 3 ? 16 : 24;

        if ((long)slots.Size != slotCount * sourceSlotSize)
        {
            throw new InvalidDataException(
                "Размеры слотов level.details не совпадают с заголовком.");
        }

        var models = new XRayDetailModel[modelCount];
        var foundModels = new bool[modelCount];

        foreach (XRayChunk mesh in XRayChunkReader.Enumerate(
                     data,
                     meshes.Offset,
                     meshes.Size))
        {
            int id = checked((int)mesh.Id);

            if (id < 0 || id >= modelCount ||
                foundModels[id])
            {
                throw new InvalidDataException(
                    $"Некорректный ID detail-модели: {id}.");
            }

            models[id] = ReadModel(
                data,
                mesh);

            foundModels[id] = true;
        }

        if (foundModels.Any(found => !found))
        {
            throw new InvalidDataException(
                "Некоторые detail-модели отсутствуют.");
        }

        var resultSlots = new XRayDetailSlot[
            checked((int)slotCount)];

        for (int i = 0; i < resultSlots.Length; i++)
        {
            int offset = slots.Offset + i * sourceSlotSize;

            resultSlots[i] = sourceVersion == 3
                ? ReadSlotV3(data, offset, modelCount)
                : ReadSlotV2(data, offset, modelCount);
        }

        return new XRayDetails(
            sourceVersion,
            offsetX,
            offsetZ,
            sizeX,
            sizeZ,
            models,
            resultSlots);
    }

    private static XRayDetailModel ReadModel(
        byte[] data,
        XRayChunk chunk)
    {
        int position = chunk.Offset;
        int end = chunk.Offset + chunk.Size;

        string shader = ReadString(
            data,
            ref position,
            end);

        string texture = ReadString(
            data,
            ref position,
            end);

        if (end - position < 20)
        {
            throw new InvalidDataException(
                "Повреждён заголовок detail-модели.");
        }

        uint flags = XRayChunkReader.UInt32(
            data,
            position);

        float minScale = XRayChunkReader.Single(
            data,
            position + 4);

        float maxScale = XRayChunkReader.Single(
            data,
            position + 8);

        int vertexCount = checked((int)XRayChunkReader.UInt32(
            data,
            position + 12));

        int indexCount = checked((int)XRayChunkReader.UInt32(
            data,
            position + 16));

        position += 20;

        if (vertexCount < 0 || vertexCount > 65_535 ||
            indexCount < 0 || indexCount > 1_000_000 ||
            indexCount % 3 != 0)
        {
            throw new InvalidDataException(
                "Некорректная геометрия detail-модели.");
        }

        long required = vertexCount * 20L +
                        indexCount * 2L;

        if (required > end - position)
        {
            throw new InvalidDataException(
                "Данные detail-модели обрезаны.");
        }

        var vertices = new XRayDetailVertex[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            float x = XRayChunkReader.Single(
                data,
                position);

            float y = XRayChunkReader.Single(
                data,
                position + 4);

            float z = XRayChunkReader.Single(
                data,
                position + 8);

            float u = XRayChunkReader.Single(
                data,
                position + 12);

            float v = XRayChunkReader.Single(
                data,
                position + 16);

            vertices[i] = new XRayDetailVertex(
                new Vector3(x, y, z),
                new Vector2(u, v));

            position += 20;
        }

        var indices = new ushort[indexCount];

        for (int i = 0; i < indexCount; i++)
        {
            indices[i] = XRayChunkReader.UInt16(
                data,
                position);

            if (indices[i] >= vertexCount)
            {
                throw new InvalidDataException(
                    "Индекс detail-модели выходит за границы.");
            }

            position += 2;
        }

        return new XRayDetailModel(
            shader,
            texture,
            flags,
            minScale,
            maxScale,
            vertices,
            indices);
    }

    private static XRayDetailSlot ReadSlotV3(
        byte[] data,
        int offset,
        int modelCount)
    {
        uint a = XRayChunkReader.UInt32(data, offset);
        uint b = XRayChunkReader.UInt32(data, offset + 4);

        float baseHeight =
            (a & 0xFFF) * 0.2f - 200f;

        float topHeight =
            baseHeight + ((a >> 12) & 0xFF) * 0.1f + 0.05f;

        return new XRayDetailSlot(
            baseHeight,
            topHeight,
            CreateLayer(
                (int)((a >> 20) & 0x3F),
                XRayChunkReader.UInt16(data, offset + 8),
                modelCount),
            CreateLayer(
                (int)((a >> 26) & 0x3F),
                XRayChunkReader.UInt16(data, offset + 10),
                modelCount),
            CreateLayer(
                (int)(b & 0x3F),
                XRayChunkReader.UInt16(data, offset + 12),
                modelCount),
            CreateLayer(
                (int)((b >> 6) & 0x3F),
                XRayChunkReader.UInt16(data, offset + 14),
                modelCount));
    }

    private static XRayDetailSlot ReadSlotV2(
        byte[] data,
        int offset,
        int modelCount)
    {
        float baseHeight = XRayChunkReader.Single(
            data,
            offset);

        float topHeight = XRayChunkReader.Single(
            data,
            offset + 4);

        return new XRayDetailSlot(
            baseHeight,
            topHeight,
            CreateLayer(
                data[offset + 8],
                XRayChunkReader.UInt16(data, offset + 9),
                modelCount),
            CreateLayer(
                data[offset + 11],
                XRayChunkReader.UInt16(data, offset + 12),
                modelCount),
            CreateLayer(
                data[offset + 14],
                XRayChunkReader.UInt16(data, offset + 15),
                modelCount),
            CreateLayer(
                data[offset + 17],
                XRayChunkReader.UInt16(data, offset + 18),
                modelCount));
    }

    private static XRayDetailLayer CreateLayer(
        int id,
        ushort density,
        int modelCount)
    {
        if (id == 63 || id == 255 ||
            id < 0 || id >= modelCount)
        {
            return new XRayDetailLayer(
                byte.MaxValue,
                0);
        }

        return new XRayDetailLayer(
            (byte)id,
            density);
    }

    private static string ReadString(
        byte[] data,
        ref int position,
        int end)
    {
        int start = position;

        while (position < end && data[position] != 0)
        {
            position++;
        }

        if (position >= end)
        {
            throw new InvalidDataException(
                "Не завершена строка detail-модели.");
        }

        string value = Encoding.UTF8.GetString(
            data,
            start,
            position - start);

        position++;

        return value;
    }
}
