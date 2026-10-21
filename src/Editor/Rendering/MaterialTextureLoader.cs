
using System.Text;
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
    private static readonly string[] Extensions =
    [
        ".dds",
        ".png",
        ".tga",
        ".jpg",
        ".jpeg"
    ];

    private readonly Dictionary<string, string> _paths =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string?> _names =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _root;
    private readonly string _logPath;

    public MaterialTextureLoader()
    {
        _root = Path.Combine(
            ProjectPaths.PacksDirectory,
            "textures");

        _logPath = Path.Combine(
            ProjectPaths.BinDirectory,
            "logs",
            "editor-textures.log");

        if (!Directory.Exists(_root))
        {
            WriteLog(
                $"Папка текстур не найдена: {_root}",
                true);

            return;
        }

        string[] files = Directory.EnumerateFiles(
                _root,
                "*",
                SearchOption.AllDirectories)
            .Where(file => Extensions.Contains(
                Path.GetExtension(file),
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => Array.FindIndex(
                Extensions,
                extension => extension.Equals(
                    Path.GetExtension(file),
                    StringComparison.OrdinalIgnoreCase)))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(
                _root,
                file);

            string key = Normalize(relative);

            if (!_paths.ContainsKey(key))
            {
                _paths[key] = file;
            }

            string name = Path.GetFileName(key);

            if (_names.TryGetValue(name, out string? existing))
            {
                if (existing is not null &&
                    !existing.Equals(
                        file,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _names[name] = null;
                }
            }
            else
            {
                _names[name] = file;
            }
        }

        WriteLog(
            $"Папка: {_root}{Environment.NewLine}" +
            $"Файлов текстур: {files.Length}{Environment.NewLine}" +
            $"Уникальных путей: {_paths.Count}",
            true);
    }

    public string? Resolve(string material)
    {
        string key = GetTextureKey(material);

        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (_paths.TryGetValue(key, out string? exact))
        {
            return exact;
        }

        string filename = Path.GetFileName(key);

        return _names.TryGetValue(filename, out string? fallback)
            ? fallback
            : null;
    }

    public void WriteMaterialReport(
        IEnumerable<string> materials)
    {
        var report = new StringBuilder();

        int found = 0;
        int missing = 0;

        foreach (string material in materials)
        {
            if (string.IsNullOrWhiteSpace(material))
            {
                continue;
            }

            string texture = GetTextureKey(material);
            string? path = Resolve(material);

            if (path is null)
            {
                missing++;

                report.AppendLine(
                    $"[MISSING] {texture} | {material}");
            }
            else
            {
                found++;

                report.AppendLine(
                    $"[FOUND] {texture} -> {path}");
            }
        }

        WriteLog(
            $"{Environment.NewLine}Проверка материалов:" +
            $"{Environment.NewLine}Найдено: {found}" +
            $"{Environment.NewLine}Отсутствует: {missing}" +
            Environment.NewLine +
            report);
    }

    public TexturePixels? Load(string material)
    {
        string? path = Resolve(material);

        if (path is null)
        {
            return null;
        }

        using Image<Rgba32> image = ReadImage(path);

        const int maxDimension = 2048;

        if (image.Width > maxDimension ||
            image.Height > maxDimension)
        {
            float scale = Math.Min(
                maxDimension / (float)image.Width,
                maxDimension / (float)image.Height);

            int width = Math.Max(
                1,
                (int)(image.Width * scale));

            int height = Math.Max(
                1,
                (int)(image.Height * scale));

            image.Mutate(context =>
                context.Resize(width, height));
        }

        byte[] pixels = new byte[
            checked(image.Width * image.Height * 4)];

        image.CopyPixelDataTo(pixels);

        return new TexturePixels(
            image.Width,
            image.Height,
            pixels);
    }

    public void ReportError(
        string material,
        Exception exception)
    {
        WriteLog(
            $"[ERROR] {material}: " +
            $"{exception.GetType().Name}: {exception.Message}");
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

    private static string GetTextureKey(string material)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            return string.Empty;
        }

        string value = material.Trim();

        int shaderSeparator = value.IndexOf('/');

        if (shaderSeparator >= 0)
        {
            value = value[(shaderSeparator + 1)..];
        }

        int lightmapSeparator = value.IndexOf(',');

        if (lightmapSeparator >= 0)
        {
            value = value[..lightmapSeparator];
        }

        return Normalize(value);
    }

    private static string Normalize(string value)
    {
        string path = value.Trim()
            .Replace('\\', '/')
            .TrimStart('/');

        if (path.StartsWith(
                "textures/",
                StringComparison.OrdinalIgnoreCase))
        {
            path = path["textures/".Length..];
        }

        string extension = Path.GetExtension(path);

        if (Extensions.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            path = path[..^extension.Length];
        }

        return path.ToLowerInvariant();
    }

    private void WriteLog(
        string message,
        bool overwrite = false)
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(_logPath)!);

            string text =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                message +
                Environment.NewLine;

            if (overwrite)
            {
                File.WriteAllText(_logPath, text);
            }
            else
            {
                File.AppendAllText(_logPath, text);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
