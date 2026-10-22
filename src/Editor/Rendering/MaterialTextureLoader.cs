using System.IO.Compression;
using System.Text;
using BCnEncoder.Decoder;
using BCnEncoder.ImageSharp;
using Core.Levels;
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
    private sealed record TextureLocation(
        string? FilePath,
        string? EntryPath,
        string Source)
    {
        public string DisplayPath =>
            FilePath ?? $"{Source}::{EntryPath}";
    }

    private static readonly string[] Extensions =
    [
        ".dds",
        ".png",
        ".tga",
        ".jpg",
        ".jpeg"
    ];

    private readonly Dictionary<string, TextureLocation> _levelFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TextureLocation> _sharedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TextureLocation?> _levelNames =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TextureLocation?> _sharedNames =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _archivePath;
    private readonly string _logPath;

    public MaterialTextureLoader(string levelName)
    {
        string packs = ProjectPaths.PacksDirectory;

        _archivePath = LevelPaths.PackagePath(levelName);

        _logPath = Path.Combine(
            ProjectPaths.BinDirectory,
            "logs",
            "editor-textures.log");

        string editableLevel = Path.Combine(
            packs,
            "levels",
            levelName);

        string importedLevel = Path.Combine(
            packs,
            "import",
            "levels",
            levelName);

        string globalTextures = Path.Combine(
            packs,
            "textures");

        IndexDirectory(
            Path.Combine(editableLevel, "materials"),
            _levelFiles,
            _levelNames);

        IndexDirectory(
            Path.Combine(editableLevel, "textures"),
            _levelFiles,
            _levelNames);

        IndexDirectory(
            Path.Combine(editableLevel, "source"),
            _levelFiles,
            _levelNames);

        IndexDirectory(
            importedLevel,
            _levelFiles,
            _levelNames);

        IndexArchive();

        IndexDirectory(
            globalTextures,
            _sharedFiles,
            _sharedNames);

        WriteLog(
            $"Уровень: {levelName}{Environment.NewLine}" +
            $"Локальных текстур: {_levelFiles.Count}" +
            Environment.NewLine +
            $"Общих текстур: {_sharedFiles.Count}" +
            Environment.NewLine +
            $"Архив: {_archivePath}",
            true);
    }

    public string? Resolve(string material)
    {
        XRayMaterialReference reference =
            XRayMaterialReference.Parse(material);

        return ResolveReference(
            reference.BaseTexture);
    }

    public string? ResolveReference(string textureName)
    {
        return FindLocation(textureName)?.DisplayPath;
    }

    public TexturePixels? Load(string material)
    {
        XRayMaterialReference reference =
            XRayMaterialReference.Parse(material);

        return LoadReference(reference.BaseTexture);
    }

    public TexturePixels? LoadAuxiliary(
        string material,
        int index)
    {
        XRayMaterialReference reference =
            XRayMaterialReference.Parse(material);

        if (index < 0 ||
            index >= reference.AuxiliaryTextures.Count)
        {
            return null;
        }

        return LoadReference(
            reference.AuxiliaryTextures[index]);
    }

    public TexturePixels? LoadReference(string textureName)
    {
        TextureLocation? location = FindLocation(textureName);

        if (location is null)
        {
            return null;
        }

        using Stream stream = OpenTexture(location);
        using Image<Rgba32> image = DecodeTexture(
            stream,
            location.DisplayPath);

        const int maximumDimension = 2048;

        if (image.Width > maximumDimension ||
            image.Height > maximumDimension)
        {
            float scale = Math.Min(
                maximumDimension / (float)image.Width,
                maximumDimension / (float)image.Height);

            int width = Math.Max(
                1,
                (int)(image.Width * scale));

            int height = Math.Max(
                1,
                (int)(image.Height * scale));

            image.Mutate(context =>
                context.Resize(width, height));
        }

        byte[] rgba = new byte[
            checked(image.Width * image.Height * 4)];

        image.CopyPixelDataTo(rgba);

        return new TexturePixels(
            image.Width,
            image.Height,
            rgba);
    }

    public void WriteMaterialReport(
        IEnumerable<string> materials)
    {
        var report = new StringBuilder();

        int found = 0;
        int missing = 0;
        int terrainMaterials = 0;
        int auxiliaryCount = 0;

        foreach (string material in materials)
        {
            if (string.IsNullOrWhiteSpace(material))
            {
                continue;
            }

            XRayMaterialReference reference =
                XRayMaterialReference.Parse(material);

            if (reference.IsTerrain)
            {
                terrainMaterials++;
            }

            auxiliaryCount += reference.AuxiliaryTextures.Count;

            report.AppendLine(
                $"[MATERIAL] Shader={reference.Shader}");

            WriteTextureStatus(
                report,
                "BASE",
                reference.BaseTexture,
                ref found,
                ref missing);

            for (int i = 0;
                 i < reference.AuxiliaryTextures.Count;
                 i++)
            {
                WriteTextureStatus(
                    report,
                    $"AUX{i + 1}",
                    reference.AuxiliaryTextures[i],
                    ref found,
                    ref missing);
            }
        }

        WriteLog(
            $"{Environment.NewLine}=== Материалы уровня ===" +
            $"{Environment.NewLine}Найдено ссылок: {found}" +
            $"{Environment.NewLine}Не найдено ссылок: {missing}" +
            $"{Environment.NewLine}Terrain-материалов: {terrainMaterials}" +
            $"{Environment.NewLine}Дополнительных карт: {auxiliaryCount}" +
            Environment.NewLine +
            report);

        WriteLevelResourceReport();
    }

    public void ReportError(
        string material,
        Exception exception)
    {
        WriteLog(
            $"[ERROR] {material}: " +
            $"{exception.GetType().Name}: {exception.Message}");
    }

    private void WriteLevelResourceReport()
    {
        var report = new StringBuilder();

        report.AppendLine();
        report.AppendLine("=== Ресурсы самого уровня ===");

        string[] specialResources =
        [
            "build_details",
            "level_lods",
            "level_lods_nm"
        ];

        foreach (string name in specialResources)
        {
            TextureLocation? location = FindLocation(name);

            report.AppendLine(location is null
                ? $"[NOT PRESENT] {name}.dds"
                : $"[FOUND] {name} -> {location.DisplayPath}");
        }

        string[] lightmaps = _levelFiles.Keys
            .Where(key => Path.GetFileName(key).StartsWith(
                "lmap#",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        report.AppendLine(
            $"Количество локальных lmap#*.dds: {lightmaps.Length}");

        foreach (string lightmap in lightmaps)
        {
            report.AppendLine(
                $"[LIGHTMAP] {lightmap}");
        }

        string[] terrainTextures = _levelFiles.Keys
            .Where(key =>
                key.StartsWith(
                    "terrain/",
                    StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(
                    "terrains/",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        report.AppendLine(
            $"Локальных terrain-текстур: {terrainTextures.Length}");

        foreach (string terrain in terrainTextures)
        {
            report.AppendLine(
                $"[TERRAIN] {terrain}");
        }

        WriteLog(report.ToString());
    }

    private void WriteTextureStatus(
        StringBuilder report,
        string category,
        string name,
        ref int found,
        ref int missing)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        TextureLocation? location = FindLocation(name);

        if (location is null)
        {
            missing++;

            report.AppendLine(
                $"[MISSING][{category}] {name}");
        }
        else
        {
            found++;

            report.AppendLine(
                $"[FOUND][{category}] {name} -> " +
                location.DisplayPath);
        }
    }

    private TextureLocation? FindLocation(string textureName)
    {
        string key = Normalize(textureName);

        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        bool localFirst =
            key.StartsWith(
                "terrain/",
                StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(
                "terrains/",
                StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(
                "lmap#",
                StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(
                "level_lods",
                StringComparison.OrdinalIgnoreCase) ||
            key.Equals(
                "build_details",
                StringComparison.OrdinalIgnoreCase);

        if (localFirst)
        {
            if (_levelFiles.TryGetValue(
                    key,
                    out TextureLocation? levelExact))
            {
                return levelExact;
            }

            if (_sharedFiles.TryGetValue(
                    key,
                    out TextureLocation? sharedExact))
            {
                return sharedExact;
            }
        }
        else
        {
            if (_sharedFiles.TryGetValue(
                    key,
                    out TextureLocation? sharedExact))
            {
                return sharedExact;
            }

            if (_levelFiles.TryGetValue(
                    key,
                    out TextureLocation? levelExact))
            {
                return levelExact;
            }
        }

        string filename = Path.GetFileName(key);

        if (_levelNames.TryGetValue(
                filename,
                out TextureLocation? levelFallback) &&
            levelFallback is not null)
        {
            return levelFallback;
        }

        if (_sharedNames.TryGetValue(
                filename,
                out TextureLocation? sharedFallback) &&
            sharedFallback is not null)
        {
            return sharedFallback;
        }

        return null;
    }

    private void IndexDirectory(
        string root,
        Dictionary<string, TextureLocation> paths,
        Dictionary<string, TextureLocation?> names)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (!IsSupportedImage(file))
            {
                continue;
            }

            string relative = Path.GetRelativePath(
                root,
                file);

            string key = Normalize(relative);

            Register(
                paths,
                names,
                key,
                new TextureLocation(
                    file,
                    null,
                    root));
        }
    }

    private void IndexArchive()
    {
        if (!File.Exists(_archivePath))
        {
            return;
        }

        using ZipArchive archive =
            ZipFile.OpenRead(_archivePath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName
                .Replace('\\', '/');

            if (!name.StartsWith(
                    "source/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsSupportedImage(name))
            {
                continue;
            }

            string relative = name["source/".Length..];
            string key = Normalize(relative);

            Register(
                _levelFiles,
                _levelNames,
                key,
                new TextureLocation(
                    null,
                    entry.FullName,
                    _archivePath));
        }
    }

    private static void Register(
        Dictionary<string, TextureLocation> paths,
        Dictionary<string, TextureLocation?> names,
        string key,
        TextureLocation location)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!paths.TryAdd(key, location))
        {
            return;
        }

        string filename = Path.GetFileName(key);

        if (names.ContainsKey(filename))
        {
            names[filename] = null;
        }
        else
        {
            names[filename] = location;
        }
    }

    private Stream OpenTexture(TextureLocation location)
    {
        if (location.FilePath is not null)
        {
            return File.OpenRead(location.FilePath);
        }

        if (location.EntryPath is null)
        {
            throw new FileNotFoundException(
                "Текстура не имеет источника.");
        }

        using ZipArchive archive =
            ZipFile.OpenRead(_archivePath);

        ZipArchiveEntry entry =
            archive.GetEntry(location.EntryPath) ??
            throw new FileNotFoundException(
                $"Не найдена текстура в архиве: {location.EntryPath}");

        var buffer = new MemoryStream();

        try
        {
            using Stream source = entry.Open();

            source.CopyTo(buffer);
            buffer.Position = 0;

            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static Image<Rgba32> DecodeTexture(
        Stream stream,
        string sourceName)
    {
        if (Path.GetExtension(sourceName).Equals(
                ".dds",
                StringComparison.OrdinalIgnoreCase))
        {
            var decoder = new BcDecoder();

            return decoder.DecodeToImageRgba32(stream);
        }

        return Image.Load<Rgba32>(stream);
    }

    private static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path);

        return Extensions.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        string path = value.Trim()
            .Replace('\\', '/')
            .TrimStart('/');

        if (path.StartsWith(
                "gamedata/textures/",
                StringComparison.OrdinalIgnoreCase))
        {
            path = path["gamedata/textures/".Length..];
        }
        else if (path.StartsWith(
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
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]" +
                Environment.NewLine +
                message +
                Environment.NewLine;

            if (overwrite)
            {
                File.WriteAllText(
                    _logPath,
                    text,
                    Encoding.UTF8);
            }
            else
            {
                File.AppendAllText(
                    _logPath,
                    text,
                    Encoding.UTF8);
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
