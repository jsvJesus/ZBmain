
using System.Text;

namespace Core.Levels.XRay;

public sealed record DetailConversionResult(
    int Models,
    int Cells,
    long ActiveLayers,
    long Triangles);

public sealed class XRayDetailConverter
{
    private readonly XRayDetailsReader _reader = new();

    public DetailConversionResult Convert(
        string sourcePath,
        Stream output)
    {
        XRayDetails details = _reader.Read(sourcePath);

        using var writer = new BinaryWriter(
            output,
            Encoding.UTF8,
            leaveOpen: true);

        writer.Write("ZDTL"u8);
        writer.Write(1);

        writer.Write(details.SizeX);
        writer.Write(details.SizeZ);
        writer.Write(details.OffsetX);
        writer.Write(details.OffsetZ);

        writer.Write(details.Models.Length);
        writer.Write(details.Slots.Length);

        long triangles = 0;

        foreach (XRayDetailModel model in details.Models)
        {
            writer.Write(model.Shader);
            writer.Write(model.Texture);
            writer.Write(model.Flags);
            writer.Write(model.MinScale);
            writer.Write(model.MaxScale);

            writer.Write(model.Vertices.Length);
            writer.Write(model.Indices.Length);

            foreach (XRayDetailVertex vertex in model.Vertices)
            {
                writer.Write(-vertex.Position.X);
                writer.Write(vertex.Position.Y);
                writer.Write(vertex.Position.Z);

                writer.Write(vertex.Uv.X);
                writer.Write(vertex.Uv.Y);
            }

            for (int i = 0; i < model.Indices.Length; i += 3)
            {
                writer.Write(model.Indices[i]);
                writer.Write(model.Indices[i + 2]);
                writer.Write(model.Indices[i + 1]);
            }

            triangles += model.Indices.Length / 3;
        }

        long activeLayers = 0;

        for (int i = 0; i < details.Slots.Length; i++)
        {
            XRayDetailSlot slot = details.Slots[i];

            int cellX = i % details.SizeX;
            int cellZ = i / details.SizeX;

            float originalX =
                (cellX - details.OffsetX) * 2f + 1f;

            float originalZ =
                (cellZ - details.OffsetZ) * 2f + 1f;

            writer.Write(-originalX);
            writer.Write(originalZ);

            writer.Write(slot.BaseHeight);
            writer.Write(slot.TopHeight);

            WriteLayer(writer, slot.Layer0, ref activeLayers);
            WriteLayer(writer, slot.Layer1, ref activeLayers);
            WriteLayer(writer, slot.Layer2, ref activeLayers);
            WriteLayer(writer, slot.Layer3, ref activeLayers);
        }

        writer.Flush();

        return new DetailConversionResult(
            details.Models.Length,
            details.Slots.Length,
            activeLayers,
            triangles);
    }

    private static void WriteLayer(
        BinaryWriter writer,
        XRayDetailLayer layer,
        ref long activeLayers)
    {
        writer.Write(layer.Model);
        writer.Write(layer.Density);

        if (layer.Model != byte.MaxValue &&
            layer.Density != 0)
        {
            activeLayers++;
        }
    }
}
