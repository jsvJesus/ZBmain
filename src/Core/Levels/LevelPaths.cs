using Core.Storage;

namespace Core.Levels;

public static class LevelPaths
{
    public static string ImportDirectory =>
        Path.Combine(
            ProjectPaths.PacksDirectory,
            "import",
            "levels");

    public static string OutputDirectory =>
        Path.Combine(
            ProjectPaths.PacksDirectory,
            "levels");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ImportDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public static string LevelDirectory(string levelName)
    {
        ValidateName(levelName);

        return Path.Combine(
            OutputDirectory,
            levelName);
    }

    public static string SourceDirectory(string levelName)
    {
        ValidateName(levelName);

        return Path.Combine(
            ImportDirectory,
            levelName);
    }

    public static string PackagePath(string levelName)
    {
        ValidateName(levelName);

        // Путь старого формата. Сохраняем для совместимости.
        return Path.Combine(
            OutputDirectory,
            levelName + ".zrlevel");
    }

    public static string OpenPath(string levelName)
    {
        string directory = LevelDirectory(levelName);

        if (File.Exists(Path.Combine(
                directory,
                "geometry",
                "render.zgeom")))
        {
            return directory;
        }

        string archive = PackagePath(levelName);

        if (File.Exists(archive))
        {
            return archive;
        }

        throw new FileNotFoundException(
            $"Уровень {levelName} не найден.");
    }

    public static IReadOnlyList<string> FindImports()
    {
        EnsureDirectories();

        return Directory
            .EnumerateDirectories(ImportDirectory)
            .Where(directory =>
                File.Exists(Path.Combine(directory, "level")) &&
                File.Exists(Path.Combine(directory, "level.geom")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> FindPackages()
    {
        EnsureDirectories();

        IEnumerable<string> folders = Directory
            .EnumerateDirectories(OutputDirectory)
            .Where(directory =>
                File.Exists(Path.Combine(
                    directory,
                    "scene.json")) &&
                File.Exists(Path.Combine(
                    directory,
                    "geometry",
                    "render.zgeom")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!);

        IEnumerable<string> archives = Directory
            .EnumerateFiles(
                OutputDirectory,
                "*.zrlevel",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension);

        return folders
            .Concat(archives)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateName(string levelName)
    {
        if (string.IsNullOrWhiteSpace(levelName) ||
            levelName is "." or ".." ||
            levelName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0 ||
            levelName.Contains('/') ||
            levelName.Contains('\\'))
        {
            throw new ArgumentException(
                "Недопустимое имя уровня.",
                nameof(levelName));
        }
    }
}
