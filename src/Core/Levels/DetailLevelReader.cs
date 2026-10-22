
using System.Numerics;
using System.Text;

namespace Core.Levels;

public readonly record struct DetailVertex(
    Vector3 Position,
    Vector2 Uv);

public readonly record struct DetailLayer(
    byte Model,
    ushort Density);

public readonly record struct DetailCell(
    Vector2 Position,
    float BaseHeight,
    float TopHeight,
    DetailLayer Layer0,
    DetailLayer Layer1,
    DetailLayer Layer2,
    DetailLayer Layer3);

public sealed record DetailModel(
    string Shader,
    string Texture,
    uint Flags,
    float MinScale,
    float MaxScale,
    DetailVertex[] Vertices,
    ushort[] Indices);

public sealed record DetailLevel(
    int SizeX,
    int SizeZ,
    int OffsetX,
    int OffsetZ,
    DetailModel[] Models,
    DetailCell[] Cells)
{
    public long ActiveLayers
    {
        get
        {
            long count = 0;

            foreach (DetailCell cell in Cells)
            {
                if (IsActive(cell.Layer0)) count++;
                if (IsActive(cell.Layer1)) count++;
                if (IsActive(cell.Layer2)) count++;
                if (IsActive(cell.Layer3)) count++;
            }

            return count;
        }
    }

    private static bool IsActive(DetailLayer layer)
    {
        return layer.Model != byte.MaxValue &&
               layer.Density != 0;
    }
}

public sealed class DetailLevelReader
{
    public DetailLevel Read(string path)
    {
        using Stream stream = File.OpenRead(path);

        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8,
            leaveOpen: true);

        if (!reader.ReadBytes(4).SequenceEqual("ZDTL"u8))
        {
            throw new InvalidDataException(
                "Неверная сигнатура details.zdetail.");
        }

        int version = reader.ReadInt32();

        if (version != 1)
        {
            throw new InvalidDataException(
                $"Неизвестная версия details.zdetail: {version}.");
        }

        int sizeX = reader.ReadInt32();
        int sizeZ = reader.ReadInt32();
        int offsetX = reader.ReadInt32();
        int offsetZ = reader.ReadInt32();

        int modelCount = ReadCount(reader, 63);
        int cellCount = ReadCount(reader, 5_000_000);

        if (sizeX <= 0 ||
            sizeZ <= 0 ||
            (long)sizeX * sizeZ != cellCount)
        {
            throw new InvalidDataException(
                "Некорректный размер сетки растительности.");
        }

        var models = new DetailModel[modelCount];

        for (int i = 0; i < modelCount; i++)
        {
            string shader = reader.ReadString();
            string texture = reader.ReadString();

            uint flags = reader.ReadUInt32();
            float minScale = reader.ReadSingle();
            float maxScale = reader.ReadSingle();

            int vertexCount = ReadCount(reader, 65_535);
            int indexCount = ReadCount(reader, 1_000_000);

            if (indexCount % 3 != 0)
            {
                throw new InvalidDataException(
                    "Некорректные индексы detail-модели.");
            }

            var vertices = new DetailVertex[vertexCount];

            for (int j = 0; j < vertexCount; j++)
            {
                Vector3 position = new(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                Vector2 uv = new(
                    reader.ReadSingle(),
                    reader.ReadSingle());

                vertices[j] = new DetailVertex(position, uv);
            }

            var indices = new ushort[indexCount];

            for (int j = 0; j < indexCount; j++)
            {
                indices[j] = reader.ReadUInt16();

                if (indices[j] >= vertexCount)
                {
                    throw new InvalidDataException(
                        "Повреждён индекс detail-модели.");
                }
            }

            models[i] = new DetailModel(
                shader,
                texture,
                flags,
                minScale,
                maxScale,
                vertices,
                indices);
        }

        var cells = new DetailCell[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            Vector2 position = new(
                reader.ReadSingle(),
                reader.ReadSingle());

            float baseHeight = reader.ReadSingle();
            float topHeight = reader.ReadSingle();

            cells[i] = new DetailCell(
                position,
                baseHeight,
                topHeight,
                ReadLayer(reader, modelCount),
                ReadLayer(reader, modelCount),
                ReadLayer(reader, modelCount),
                ReadLayer(reader, modelCount));
        }

        return new DetailLevel(
            sizeX,
            sizeZ,
            offsetX,
            offsetZ,
            models,
            cells);
    }

    private static DetailLayer ReadLayer(
        BinaryReader reader,
        int modelCount)
    {
        byte model = reader.ReadByte();
        ushort density = reader.ReadUInt16();

        if (model != byte.MaxValue &&
            model >= modelCount)
        {
            throw new InvalidDataException(
                "Некорректная ссылка на detail-модель.");
        }

        return new DetailLayer(
            model,
            density);
    }

    private static int ReadCount(
        BinaryReader reader,
        int maximum)
    {
        int count = reader.ReadInt32();

        if (count < 0 || count > maximum)
        {
            throw new InvalidDataException(
                $"Некорректное количество элементов: {count}.");
        }

        return count;
    }
}
