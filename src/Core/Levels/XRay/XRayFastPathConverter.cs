
using System.Text;

namespace Core.Levels.XRay;

public sealed class XRayFastPathConverter
{
    private readonly XRayGeometryConverter _converter = new();

    public VisualConversionResult Convert(
        string levelFile,
        string geomxFile,
        Stream output)
    {
        byte[] level = File.ReadAllBytes(levelFile);

        XRayChunk header = XRayChunkReader.Required(
            level, 0, level.Length, 1);

        XRayChunk shaders = XRayChunkReader.Required(
            level, 0, level.Length, 2);

        XRayChunk visuals = XRayChunkReader.Required(
            level, 0, level.Length, 3);

        string temporaryLevel = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".level");

        try
        {
            using (FileStream file = File.Create(temporaryLevel))
            using (var writer = new BinaryWriter(
                       file, Encoding.UTF8, leaveOpen: true))
            {
                WriteChunk(
                    writer,
                    1,
                    level.AsSpan(header.Offset, header.Size));

                WriteChunk(
                    writer,
                    2,
                    level.AsSpan(shaders.Offset, shaders.Size));

                using var visualData = new MemoryStream();
                using var visualWriter = new BinaryWriter(
                    visualData,
                    Encoding.UTF8,
                    leaveOpen: true);

                uint newVisualId = 0;

                foreach (XRayChunk visual in XRayChunkReader.Enumerate(
                             level,
                             visuals.Offset,
                             visuals.Size))
                {
                    XRayChunk? fast = XRayChunkReader.Find(
                        level,
                        visual.Offset,
                        visual.Size,
                        22);

                    XRayChunk? visualHeader = XRayChunkReader.Find(
                        level,
                        visual.Offset,
                        visual.Size,
                        1);

                    if (fast is null ||
                        visualHeader is null ||
                        fast.Value.Size < 32)
                    {
                        continue;
                    }

                    using var convertedVisual = new MemoryStream();
                    using var convertedWriter = new BinaryWriter(
                        convertedVisual,
                        Encoding.UTF8,
                        leaveOpen: true);

                    convertedWriter.Write(
                        level,
                        fast.Value.Offset,
                        fast.Value.Size);

                    WriteChunk(
                        convertedWriter,
                        1,
                        level.AsSpan(
                            visualHeader.Value.Offset,
                            visualHeader.Value.Size));

                    WriteChunk(
                        visualWriter,
                        newVisualId++,
                        convertedVisual.ToArray());
                }

                WriteChunk(
                    writer,
                    3,
                    visualData.ToArray());
            }

            return _converter.Convert(
                temporaryLevel,
                geomxFile,
                output,
                Stream.Null);
        }
        finally
        {
            if (File.Exists(temporaryLevel))
            {
                File.Delete(temporaryLevel);
            }
        }
    }

    private static void WriteChunk(
        BinaryWriter writer,
        uint id,
        ReadOnlySpan<byte> data)
    {
        writer.Write(id);
        writer.Write(checked((uint)data.Length));
        writer.Write(data);
    }
}
