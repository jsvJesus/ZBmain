using BCnEncoder.Decoder;
using BCnEncoder.ImageSharp;
using Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Editor.Rendering;

public sealed record TexturePixels(
    int Width,
    int Height,
    byte[] Rgba);

public sealed class MaterialTextureLoader
{
    private readonly Dictionary<string, string> _files =
        new(StringComparer.OrdinalIgnoreCase);

    public MaterialTextureLoader()
    {
        string root = Path.Combine(
            ProjectPaths.PacksDirectory,
            "textures");

        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);

            if (!extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, file)
                .Replace('\\', '/');

            string key = Path.ChangeExtension(relative, null)!
                .Replace('\\', '/');

            if (!_files.ContainsKey(key))
            {
                _files[key] = file;
            }
        }
    }

    public string? Resolve(string material)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            return null;
        }

        string textures = material;

        int shaderSeparator = textures.IndexOf('/');

        if (shaderSeparator >= 0)
        {
            textures = textures[(shaderSeparator + 1)..];
        }

        int lightmapSeparator = textures.IndexOf(',');

        if (lightmapSeparator >= 0)
        {
            textures = textures[..lightmapSeparator];
        }

        string key = textures.Trim()
            .Replace('\\', '/')
            .TrimStart('/');

        key = Path.ChangeExtension(key, null)!
            .Replace('\\', '/');

        return _files.GetValueOrDefault(key);
    }

    public TexturePixels? Load(string material)
    {
        string? path = Resolve(material);

        if (path is null)
        {
            return null;
        }

        using Image<Rgba32> image = ReadImage(path);

        const int maximumDimension = 2048;

        int width = image.Width;
        int height = image.Height;

        if (width > maximumDimension ||
            height > maximumDimension)
        {
            float scale = Math.Min(
                maximumDimension / (float)width,
                maximumDimension / (float)height);

            int newWidth = Math.Max(1, (int)(width * scale));
            int newHeight = Math.Max(1, (int)(height * scale));

            image.Mutate(context =>
                context.Resize(newWidth, newHeight));
        }

        byte[] data = new byte[
            checked(image.Width * image.Height * 4)];

        image.CopyPixelDataTo(data);

        return new TexturePixels(
            image.Width,
            image.Height,
            data);
    }

    private static Image<Rgba32> ReadImage(string path)
    {
        using FileStream stream = File.OpenRead(path);

        if (Path.GetExtension(path).Equals(
                ".dds",
                StringComparison.OrdinalIgnoreCase))
        {
            var decoder = new BcDecoder();

            return decoder.DecodeToImageRgba32(stream);
        }

        return Image.Load<Rgba32>(stream);
    }
}
