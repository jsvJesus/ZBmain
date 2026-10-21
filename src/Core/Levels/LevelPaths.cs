using Core.Storage;

namespace Core.Levels;

public static class LevelPaths
{
    public static string ImportDirectory =>
        Path.Combine(ProjectPaths.PacksDirectory, "import", "levels");

    public static string OutputDirectory =>
        Path.Combine(ProjectPaths.PacksDirectory, "levels");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ImportDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public static IReadOnlyList<string> FindImports()
    {
        EnsureDirectories();

        return Directory.EnumerateDirectories(ImportDirectory)
            .Where(path =>
                File.Exists(Path.Combine(path, "level")) ||
                Directory.EnumerateFiles(
                    path,
                    "level.*",
                    SearchOption.TopDirectoryOnly).Any())
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> FindPackages()
    {
        EnsureDirectories();

        return Directory.EnumerateFiles(
                OutputDirectory,
                "*.zrlevel",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string PackagePath(string levelName)
    {
        if (string.IsNullOrWhiteSpace(levelName) ||
            levelName is "." or ".." ||
            levelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            levelName.Contains('/') ||
            levelName.Contains('\\'))
        {
            throw new ArgumentException(
                "Недопустимое имя уровня.",
                nameof(levelName));
        }

        return Path.Combine(
            OutputDirectory,
            levelName + ".zrlevel");
    }
}
