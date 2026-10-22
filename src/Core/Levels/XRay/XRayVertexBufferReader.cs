
using System.Numerics;

namespace Core.Levels.XRay;

internal sealed record XRayVertexBuffer(
    Vector3[] Positions,
    Vector2[] Uvs,
    Vector2[] LightmapUvs);

internal static class XRayVertexBufferReader
{
    private readonly record struct Element(
        int Offset,
        int Type,
        int Usage,
        int UsageIndex);

    public static XRayVertexBuffer[] Read(byte[] bytes)
    {
        XRayChunk chunk = XRayChunkReader.Required(
            bytes,
            0,
            bytes.Length,
            9);

        int position = chunk.Offset;
        int end = chunk.Offset + chunk.Size;

        int bufferCount = checked(
            (int)XRayChunkReader.UInt32(bytes, position));

        position += 4;

        if (bufferCount < 0 || bufferCount > 512)
        {
            throw new InvalidDataException(
                "Неверное количество вершинных буферов.");
        }

        var result = new XRayVertexBuffer[bufferCount];

        for (int bufferIndex = 0;
             bufferIndex < bufferCount;
             bufferIndex++)
        {
            var elements = new List<Element>();
            int stride = 0;

            while (true)
            {
                if (position + 8 > end)
                {
                    throw new InvalidDataException(
                        "Повреждена декларация вершин.");
                }

                int stream = XRayChunkReader.UInt16(
                    bytes,
                    position);

                int offset = XRayChunkReader.UInt16(
                    bytes,
                    position + 2);

                int type = bytes[position + 4];
                int usage = bytes[position + 6];
                int usageIndex = bytes[position + 7];

                position += 8;

                if (type == 17)
                {
                    break;
                }

                if (stream != 0)
                {
                    throw new NotSupportedException(
                        "Несколько потоков вершин не поддерживаются.");
                }

                int size = type switch
                {
                    1 => 8,
                    2 => 12,
                    3 => 16,
                    4 => 4,
                    6 => 4,
                    7 => 8,
                    _ => throw new NotSupportedException(
                        $"Неизвестный формат вершины D3D9: {type}.")
                };

                stride = Math.Max(stride, offset + size);

                elements.Add(new Element(
                    offset,
                    type,
                    usage,
                    usageIndex));
            }

            if (stride <= 0 || stride > 256 ||
                position + 4 > end)
            {
                throw new InvalidDataException(
                    "Неверный размер вершины.");
            }

            int vertexCount = checked(
                (int)XRayChunkReader.UInt32(
                    bytes,
                    position));

            position += 4;

            long next = (long)position +
                        (long)vertexCount * stride;

            if (vertexCount < 0 ||
                vertexCount > 20_000_000 ||
                next > end)
            {
                throw new InvalidDataException(
                    "Повреждён вершинный буфер.");
            }

            Element? positionElement =
                Find(elements, 0, 0);

            Element? uvElement =
                Find(elements, 5, 0);

            Element? lightmapElement =
                Find(elements, 5, 1);

            Element? tangentElement =
                Find(elements, 6, 0);

            Element? binormalElement =
                Find(elements, 7, 0);

            if (positionElement is null ||
                positionElement.Value.Type != 2)
            {
                throw new InvalidDataException(
                    "Не найдена позиция FLOAT3.");
            }

            var positions = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var lightmapUvs = new Vector2[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                int start = position + i * stride;

                int p = start + positionElement.Value.Offset;

                positions[i] = new Vector3(
                    XRayChunkReader.Single(bytes, p),
                    XRayChunkReader.Single(bytes, p + 4),
                    XRayChunkReader.Single(bytes, p + 8));

                if (uvElement.HasValue)
                {
                    uvs[i] = ReadPrimaryUv(
                        bytes,
                        start,
                        uvElement.Value,
                        tangentElement,
                        binormalElement);
                }

                if (lightmapElement.HasValue)
                {
                    lightmapUvs[i] = ReadLightmapUv(
                        bytes,
                        start,
                        lightmapElement.Value);
                }
            }

            result[bufferIndex] = new XRayVertexBuffer(
                positions,
                uvs,
                lightmapUvs);

            position = checked((int)next);
        }

        return result;
    }

    private static Element? Find(
        IEnumerable<Element> elements,
        int usage,
        int usageIndex)
    {
        foreach (Element element in elements)
        {
            if (element.Usage == usage &&
                element.UsageIndex == usageIndex)
            {
                return element;
            }
        }

        return null;
    }

    private static Vector2 ReadPrimaryUv(
        byte[] bytes,
        int vertexStart,
        Element uv,
        Element? tangent,
        Element? binormal)
    {
        int offset = vertexStart + uv.Offset;

        if (uv.Type == 1)
        {
            return new Vector2(
                XRayChunkReader.Single(bytes, offset),
                XRayChunkReader.Single(bytes, offset + 4));
        }

        float coefficient = uv.Type switch
        {
            6 => 1024f,
            7 => 2048f,
            _ => throw new NotSupportedException(
                $"Неподдерживаемый формат UV0: {uv.Type}.")
        };

        float u = ReadInt16(bytes, offset) / coefficient;
        float v = ReadInt16(bytes, offset + 2) / coefficient;

        if (tangent.HasValue &&
            binormal.HasValue &&
            tangent.Value.Type == 4 &&
            binormal.Value.Type == 4)
        {
            byte correctU = bytes[
                vertexStart + tangent.Value.Offset + 3];

            byte correctV = bytes[
                vertexStart + binormal.Value.Offset + 3];

            const float correctionScale = 1f / 1024f;

            u += correctU / 255f * correctionScale;
            v += correctV / 255f * correctionScale;
        }

        return new Vector2(u, v);
    }

    private static Vector2 ReadLightmapUv(
        byte[] bytes,
        int vertexStart,
        Element uv)
    {
        int offset = vertexStart + uv.Offset;

        return uv.Type switch
        {
            1 => new Vector2(
                XRayChunkReader.Single(bytes, offset),
                XRayChunkReader.Single(bytes, offset + 4)),

            6 => new Vector2(
                ReadInt16(bytes, offset) / 32767f,
                ReadInt16(bytes, offset + 2) / 32767f),

            _ => throw new NotSupportedException(
                $"Неподдерживаемый формат UV1: {uv.Type}.")
        };
    }

    private static short ReadInt16(
        byte[] bytes,
        int offset)
    {
        return unchecked((short)XRayChunkReader.UInt16(
            bytes,
            offset));
    }
}
