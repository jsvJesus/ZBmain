
using System.Numerics;
using System.Text;

namespace Core.Levels.XRay;

public sealed class XRayLodConverter
{
    private readonly record struct LodSurface(
        int Material,
        int VertexOffset,
        int IndexOffset);

    public int Convert(string levelFile, Stream output)
    {
        byte[] bytes = File.ReadAllBytes(levelFile);

        XRayChunk visuals = XRayChunkReader.Required(
            bytes, 0, bytes.Length, 3);

        XRayChunk shaders = XRayChunkReader.Required(
            bytes, 0, bytes.Length, 2);

        string[] materials = ReadMaterials(bytes, shaders);

        var vertices = new List<float>();
        var indices = new List<ushort>();
        var surfaces = new List<LodSurface>();

        foreach (XRayChunk visual in XRayChunkReader.Enumerate(
                     bytes,
                     visuals.Offset,
                     visuals.Size))
        {
            XRayChunk? header = XRayChunkReader.Find(
                bytes, visual.Offset, visual.Size, 1);

            XRayChunk? definition = XRayChunkReader.Find(
                bytes, visual.Offset, visual.Size, 11);

            if (header is null ||
                definition is null ||
                header.Value.Size < 4)
            {
                continue;
            }

            int format = bytes[header.Value.Offset];
            int type = bytes[header.Value.Offset + 1];

            if (format != 4 || type != 6)
            {
                continue;
            }

            if (definition.Value.Size != 896)
            {
                throw new InvalidDataException(
                    "Некорректный размер LODDEF2.");
            }

            int material = XRayChunkReader.UInt16(
                bytes,
                header.Value.Offset + 2);

            int vertexOffset = vertices.Count / 5;
            int indexOffset = indices.Count;

            for (int face = 0; face < 8; face++)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    int p = definition.Value.Offset +
                            (face * 4 + corner) * 28;

                    vertices.Add(XRayChunkReader.Single(bytes, p));
                    vertices.Add(XRayChunkReader.Single(bytes, p + 4));
                    vertices.Add(XRayChunkReader.Single(bytes, p + 8));
                    vertices.Add(XRayChunkReader.Single(bytes, p + 12));
                    vertices.Add(XRayChunkReader.Single(bytes, p + 16));
                }

                ushort start = (ushort)(face * 4);

                indices.Add(start);
                indices.Add((ushort)(start + 1));
                indices.Add((ushort)(start + 2));

                indices.Add(start);
                indices.Add((ushort)(start + 2));
                indices.Add((ushort)(start + 3));
            }

            surfaces.Add(new LodSurface(
                material,
                vertexOffset,
                indexOffset));
        }

        using var writer = new BinaryWriter(
            output,
            Encoding.UTF8,
            leaveOpen: true);

        writer.Write("ZRGM"u8);
        writer.Write(1);
        writer.Write(materials.Length);
        writer.Write(1);
        writer.Write(1);
        writer.Write(surfaces.Count);

        foreach (string material in materials)
        {
            writer.Write(material);
        }

        writer.Write(vertices.Count / 5);

        foreach (float value in vertices)
        {
            writer.Write(value);
        }

        writer.Write(indices.Count);

        foreach (ushort index in indices)
        {
            writer.Write(index);
        }

        foreach (LodSurface surface in surfaces)
        {
            writer.Write(surface.Material);
            writer.Write(6);
            writer.Write(0);
            writer.Write(surface.VertexOffset);
            writer.Write(32);
            writer.Write(0);
            writer.Write(surface.IndexOffset);
            writer.Write(48);

            Matrix4x4 matrix = Matrix4x4.Identity;

            writer.Write(matrix.M11);
            writer.Write(matrix.M12);
            writer.Write(matrix.M13);
            writer.Write(matrix.M14);
            writer.Write(matrix.M21);
            writer.Write(matrix.M22);
            writer.Write(matrix.M23);
            writer.Write(matrix.M24);
            writer.Write(matrix.M31);
            writer.Write(matrix.M32);
            writer.Write(matrix.M33);
            writer.Write(matrix.M34);
            writer.Write(matrix.M41);
            writer.Write(matrix.M42);
            writer.Write(matrix.M43);
            writer.Write(matrix.M44);
        }

        return surfaces.Count;
    }

    private static string[] ReadMaterials(
        byte[] bytes,
        XRayChunk chunk)
    {
        int count = checked((int)XRayChunkReader.UInt32(
            bytes,
            chunk.Offset));

        if (count < 0 || count > 100_000)
        {
            throw new InvalidDataException(
                "Некорректный список материалов.");
        }

        var materials = new string[count];

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
                    "Повреждён список материалов.");
            }

            materials[i] = Encoding.UTF8.GetString(
                bytes,
                start,
                position - start);

            position++;
        }

        return materials;
    }
}
