
using System.Numerics;
using System.Text;

namespace Core.Levels.XRay;

public sealed record VisualConversionResult(
    int Meshes,
    long Triangles,
    int Materials);

public sealed class XRayGeometryConverter
{
    private readonly record struct VertexBuffer(
        Vector3[] Positions,
        Vector2[] Uvs);

    private readonly record struct Surface(
        int Material,
        int Type,
        int VertexBuffer,
        int VertexOffset,
        int VertexCount,
        int IndexBuffer,
        int IndexOffset,
        int IndexCount,
        Matrix4x4 Transform);

    private readonly record struct SampleTriangle(
        Vector3 A,
        Vector3 B,
        Vector3 C,
        ushort Material);

    public VisualConversionResult Convert(
        string levelFile,
        string geomFile,
        Stream geometryOutput,
        Stream previewOutput)
    {
        byte[] level = File.ReadAllBytes(levelFile);
        byte[] geom = File.ReadAllBytes(geomFile);

        XRayChunk header = XRayChunkReader.Required(
            level,
            0,
            level.Length,
            1);

        ushort version = XRayChunkReader.UInt16(
            level,
            header.Offset);

        if (version != 14)
        {
            throw new NotSupportedException(
                $"Визуальная геометрия X-Ray v{version} пока не поддерживается.");
        }

        XRayChunk geometryHeader = XRayChunkReader.Required(
            geom,
            0,
            geom.Length,
            1);

        if (XRayChunkReader.UInt16(
                geom,
                geometryHeader.Offset) != version)
        {
            throw new InvalidDataException(
                "Версии level и level.geom не совпадают.");
        }

        string[] materials = ReadMaterials(level);
        VertexBuffer[] vertices = ReadVertices(geom);
        ushort[][] indices = ReadIndices(geom);

        Surface[] surfaces = ReadSurfaces(
            level,
            vertices,
            indices);

        WriteGeometry(
            geometryOutput,
            materials,
            vertices,
            indices,
            surfaces);

        WritePreview(
            previewOutput,
            vertices,
            indices,
            surfaces);

        return new VisualConversionResult(
            surfaces.Length,
            surfaces.Sum(surface =>
                (long)surface.IndexCount / 3),
            materials.Length);
    }

    private static string[] ReadMaterials(byte[] bytes)
    {
        XRayChunk chunk = XRayChunkReader.Required(
            bytes,
            0,
            bytes.Length,
            2);

        int count = checked((int)XRayChunkReader.UInt32(
            bytes,
            chunk.Offset));

        if (count > 100_000)
        {
            throw new InvalidDataException(
                "Неверное количество материалов.");
        }

        string[] materials = new string[count];
        int position = chunk.Offset + 4;
        int end = chunk.Offset + chunk.Size;

        for (int i = 0; i < count; i++)
        {
            int start = position;

            while (position < end && bytes[position] != 0)
            {
                position++;
            }

            if (position >= end)
            {
                throw new InvalidDataException(
                    "Не завершена строка материала.");
            }

            materials[i] = Encoding.UTF8.GetString(
                bytes,
                start,
                position - start);

            position++;
        }

        return materials;
    }

    private static VertexBuffer[] ReadVertices(byte[] bytes)
    {
        XRayChunk chunk = XRayChunkReader.Required(
            bytes,
            0,
            bytes.Length,
            9);

        int position = chunk.Offset;
        int end = chunk.Offset + chunk.Size;

        int count = checked((int)XRayChunkReader.UInt32(
            bytes,
            position));

        position += 4;

        if (count > 512)
        {
            throw new InvalidDataException(
                "Неверное количество вершинных буферов.");
        }

        var buffers = new VertexBuffer[count];

        for (int bufferIndex = 0;
             bufferIndex < count;
             bufferIndex++)
        {
            int stride = 0;
            int positionOffset = -1;
            int uvOffset = -1;
            int uvType = 0;

            while (true)
            {
                if (position + 8 > end)
                {
                    throw new InvalidDataException(
                        "Повреждена декларация вершины.");
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

                int size = type switch
                {
                    1 => 8,
                    2 => 12,
                    3 => 16,
                    4 => 4,
                    6 => 4,
                    7 => 8,
                    _ => throw new NotSupportedException(
                        $"D3D vertex type {type} не поддерживается.")
                };

                if (stream != 0)
                {
                    throw new NotSupportedException(
                        "Несколько потоков вершин пока не поддерживаются.");
                }

                stride = Math.Max(stride, offset + size);

                if (usage == 0 && type == 2)
                {
                    positionOffset = offset;
                }

                if (usage == 5 && usageIndex == 0)
                {
                    uvOffset = offset;
                    uvType = type;
                }
            }

            if (positionOffset < 0 ||
                stride <= 0 ||
                stride > 256 ||
                position + 4 > end)
            {
                throw new InvalidDataException(
                    "Нет позиции вершины или неверный stride.");
            }

            int vertexCount = checked(
                (int)XRayChunkReader.UInt32(bytes, position));

            position += 4;

            long next = (long)position +
                        (long)vertexCount * stride;

            if (vertexCount > 20_000_000 || next > end)
            {
                throw new InvalidDataException(
                    "Повреждён вершинный буфер.");
            }

            var points = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                int start = position + i * stride;
                int p = start + positionOffset;

                points[i] = new Vector3(
                    XRayChunkReader.Single(bytes, p),
                    XRayChunkReader.Single(bytes, p + 4),
                    XRayChunkReader.Single(bytes, p + 8));

                if (uvOffset < 0)
                {
                    continue;
                }

                int t = start + uvOffset;

                uvs[i] = uvType switch
                {
                    1 => new Vector2(
                        XRayChunkReader.Single(bytes, t),
                        XRayChunkReader.Single(bytes, t + 4)),

                    6 => new Vector2(
                        (short)XRayChunkReader.UInt16(bytes, t) / 1024f,
                        (short)XRayChunkReader.UInt16(bytes, t + 2) / 1024f),

                    7 => new Vector2(
                        (short)XRayChunkReader.UInt16(bytes, t) / 2048f,
                        (short)XRayChunkReader.UInt16(bytes, t + 2) / 2048f),

                    _ => Vector2.Zero
                };
            }

            buffers[bufferIndex] = new VertexBuffer(
                points,
                uvs);

            position = checked((int)next);
        }

        return buffers;
    }

    private static ushort[][] ReadIndices(byte[] bytes)
    {
        XRayChunk chunk = XRayChunkReader.Required(
            bytes,
            0,
            bytes.Length,
            10);

        int position = chunk.Offset;
        int end = chunk.Offset + chunk.Size;

        int count = checked((int)XRayChunkReader.UInt32(
            bytes,
            position));

        position += 4;

        if (count > 512)
        {
            throw new InvalidDataException(
                "Неверное количество индексных буферов.");
        }

        var buffers = new ushort[count][];

        for (int i = 0; i < count; i++)
        {
            if (position + 4 > end)
            {
                throw new InvalidDataException(
                    "Неполный индексный буфер.");
            }

            int indexCount = checked(
                (int)XRayChunkReader.UInt32(bytes, position));

            position += 4;

            if ((long)position + 2L * indexCount > end)
            {
                throw new InvalidDataException(
                    "Неверные размеры индексов.");
            }

            var values = new ushort[indexCount];

            Buffer.BlockCopy(
                bytes,
                position,
                values,
                0,
                indexCount * 2);

            buffers[i] = values;
            position += indexCount * 2;
        }

        return buffers;
    }

    private static Surface[] ReadSurfaces(
        byte[] bytes,
        VertexBuffer[] vertices,
        ushort[][] indices)
    {
        XRayChunk chunk = XRayChunkReader.Required(
            bytes,
            0,
            bytes.Length,
            3);

        var result = new List<Surface>();

        foreach (XRayChunk visual in XRayChunkReader.Enumerate(
                     bytes,
                     chunk.Offset,
                     chunk.Size))
        {
            XRayChunk? header = XRayChunkReader.Find(
                bytes,
                visual.Offset,
                visual.Size,
                1);

            if (header is null || header.Value.Size < 4)
            {
                continue;
            }

            int format = bytes[header.Value.Offset];
            int modelType = bytes[header.Value.Offset + 1];

            int materialId = XRayChunkReader.UInt16(
                bytes,
                header.Value.Offset + 2);

            if (format != 4)
            {
                continue;
            }

            XRayChunk? container = XRayChunkReader.Find(
                bytes,
                visual.Offset,
                visual.Size,
                21);

            int vertexBuffer;
            int vertexOffset;
            int vertexCount;
            int indexBuffer;
            int indexOffset;
            int indexCount;

            if (container.HasValue && container.Value.Size >= 24)
            {
                int p = container.Value.Offset;

                vertexBuffer = checked((int)XRayChunkReader.UInt32(bytes, p));
                vertexOffset = checked((int)XRayChunkReader.UInt32(bytes, p + 4));
                vertexCount = checked((int)XRayChunkReader.UInt32(bytes, p + 8));

                indexBuffer = checked((int)XRayChunkReader.UInt32(bytes, p + 12));
                indexOffset = checked((int)XRayChunkReader.UInt32(bytes, p + 16));
                indexCount = checked((int)XRayChunkReader.UInt32(bytes, p + 20));
            }
            else
            {
                XRayChunk? vc = XRayChunkReader.Find(
                    bytes,
                    visual.Offset,
                    visual.Size,
                    7);

                XRayChunk? ic = XRayChunkReader.Find(
                    bytes,
                    visual.Offset,
                    visual.Size,
                    8);

                if (vc is null ||
                    ic is null ||
                    vc.Value.Size < 12 ||
                    ic.Value.Size < 12)
                {
                    continue;
                }

                int v = vc.Value.Offset;
                int i = ic.Value.Offset;

                vertexBuffer = checked((int)XRayChunkReader.UInt32(bytes, v));
                vertexOffset = checked((int)XRayChunkReader.UInt32(bytes, v + 4));
                vertexCount = checked((int)XRayChunkReader.UInt32(bytes, v + 8));

                indexBuffer = checked((int)XRayChunkReader.UInt32(bytes, i));
                indexOffset = checked((int)XRayChunkReader.UInt32(bytes, i + 4));
                indexCount = checked((int)XRayChunkReader.UInt32(bytes, i + 8));
            }

            if (vertexBuffer >= vertices.Length ||
                indexBuffer >= indices.Length ||
                (long)vertexOffset + vertexCount >
                    vertices[vertexBuffer].Positions.Length ||
                (long)indexOffset + indexCount >
                    indices[indexBuffer].Length)
            {
                throw new InvalidDataException(
                    "Ссылка на буфер выходит за границы.");
            }

            Matrix4x4 transform = Matrix4x4.Identity;

            if (modelType is 7 or 11)
            {
                XRayChunk? tree = XRayChunkReader.Find(
                    bytes,
                    visual.Offset,
                    visual.Size,
                    12);

                if (tree.HasValue && tree.Value.Size >= 64)
                {
                    int p = tree.Value.Offset;
                    float[] values = new float[16];

                    for (int i = 0; i < 16; i++)
                    {
                        values[i] = XRayChunkReader.Single(
                            bytes,
                            p + i * 4);
                    }

                    transform = new Matrix4x4(
                        values[0], values[1], values[2], values[3],
                        values[4], values[5], values[6], values[7],
                        values[8], values[9], values[10], values[11],
                        values[12], values[13], values[14], values[15]);
                }
            }

            if (indexCount >= 3)
            {
                result.Add(new Surface(
                    materialId,
                    modelType,
                    vertexBuffer,
                    vertexOffset,
                    vertexCount,
                    indexBuffer,
                    indexOffset,
                    indexCount,
                    transform));
            }
        }

        return result.ToArray();
    }

    private static void WriteGeometry(
        Stream output,
        string[] materials,
        VertexBuffer[] vertices,
        ushort[][] indices,
        Surface[] surfaces)
    {
        using var writer = new BinaryWriter(
            output,
            Encoding.UTF8,
            leaveOpen: true);

        writer.Write("ZRGM"u8);
        writer.Write(1);
        writer.Write(materials.Length);
        writer.Write(vertices.Length);
        writer.Write(indices.Length);
        writer.Write(surfaces.Length);

        foreach (string material in materials)
        {
            writer.Write(material);
        }

        foreach (VertexBuffer buffer in vertices)
        {
            writer.Write(buffer.Positions.Length);

            for (int i = 0; i < buffer.Positions.Length; i++)
            {
                Vector3 point = buffer.Positions[i];
                Vector2 uv = buffer.Uvs[i];

                writer.Write(point.X);
                writer.Write(point.Y);
                writer.Write(point.Z);
                writer.Write(uv.X);
                writer.Write(uv.Y);
            }
        }

        foreach (ushort[] buffer in indices)
        {
            writer.Write(buffer.Length);

            foreach (ushort index in buffer)
            {
                writer.Write(index);
            }
        }

        foreach (Surface surface in surfaces)
        {
            writer.Write(surface.Material);
            writer.Write(surface.Type);
            writer.Write(surface.VertexBuffer);
            writer.Write(surface.VertexOffset);
            writer.Write(surface.VertexCount);
            writer.Write(surface.IndexBuffer);
            writer.Write(surface.IndexOffset);
            writer.Write(surface.IndexCount);

            Matrix4x4 m = surface.Transform;

            writer.Write(m.M11);
            writer.Write(m.M12);
            writer.Write(m.M13);
            writer.Write(m.M14);
            writer.Write(m.M21);
            writer.Write(m.M22);
            writer.Write(m.M23);
            writer.Write(m.M24);
            writer.Write(m.M31);
            writer.Write(m.M32);
            writer.Write(m.M33);
            writer.Write(m.M34);
            writer.Write(m.M41);
            writer.Write(m.M42);
            writer.Write(m.M43);
            writer.Write(m.M44);
        }
    }

    private static void WritePreview(
        Stream output,
        VertexBuffer[] vertices,
        ushort[][] indices,
        Surface[] surfaces)
    {
        const int maxTriangles = 75_000;

        long total = surfaces.Sum(surface =>
            (long)surface.IndexCount / 3);

        var triangles = new List<SampleTriangle>();
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);

        foreach (Surface surface in surfaces)
        {
            int count = surface.IndexCount / 3;

            int samples = Math.Min(
                count,
                Math.Max(
                    1,
                    (int)Math.Round(
                        count * (double)maxTriangles /
                        Math.Max(1L, total))));

            ushort[] buffer = indices[surface.IndexBuffer];
            Vector3[] points = vertices[surface.VertexBuffer].Positions;

            for (int j = 0; j < samples; j++)
            {
                int index = surface.IndexOffset +
                            (int)((long)j * count / samples) * 3;

                int a = buffer[index];
                int b = buffer[index + 1];
                int c = buffer[index + 2];

                if (a >= surface.VertexCount ||
                    b >= surface.VertexCount ||
                    c >= surface.VertexCount)
                {
                    continue;
                }

                Vector3 pa = Vector3.Transform(
                    points[surface.VertexOffset + a],
                    surface.Transform);

                Vector3 pb = Vector3.Transform(
                    points[surface.VertexOffset + b],
                    surface.Transform);

                Vector3 pc = Vector3.Transform(
                    points[surface.VertexOffset + c],
                    surface.Transform);

                if (!IsFinite(pa) ||
                    !IsFinite(pb) ||
                    !IsFinite(pc))
                {
                    continue;
                }

                triangles.Add(new SampleTriangle(
                    pa,
                    pb,
                    pc,
                    (ushort)surface.Material));

                minimum = Vector3.Min(
                    minimum,
                    Vector3.Min(pa, Vector3.Min(pb, pc)));

                maximum = Vector3.Max(
                    maximum,
                    Vector3.Max(pa, Vector3.Max(pb, pc)));
            }
        }

        if (triangles.Count == 0)
        {
            minimum = new Vector3(-50);
            maximum = new Vector3(50);
        }

        using var writer = new BinaryWriter(
            output,
            Encoding.UTF8,
            leaveOpen: true);

        writer.Write("ZPRV"u8);
        writer.Write(1);
        writer.Write(triangles.Count);

        writer.Write(minimum.X);
        writer.Write(minimum.Y);
        writer.Write(minimum.Z);
        writer.Write(maximum.X);
        writer.Write(maximum.Y);
        writer.Write(maximum.Z);

        foreach (SampleTriangle triangle in triangles)
        {
            writer.Write(triangle.A.X);
            writer.Write(triangle.A.Y);
            writer.Write(triangle.A.Z);
            writer.Write(triangle.B.X);
            writer.Write(triangle.B.Y);
            writer.Write(triangle.B.Z);
            writer.Write(triangle.C.X);
            writer.Write(triangle.C.Y);
            writer.Write(triangle.C.Z);
            writer.Write(triangle.Material);
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }
}
