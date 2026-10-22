namespace Core.Levels;

public static class LevelTexturePolicy
{
    public static bool IsBakedLightingTexture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalized = path.Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        string filename = Path.GetFileName(normalized);
        string extension = Path.GetExtension(filename);

        if (!string.IsNullOrEmpty(extension) &&
            !extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string name = Path.GetFileNameWithoutExtension(filename);

        if (name.StartsWith(
                "lmap#",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] directories = normalized.Split('/');

        bool isTerrain = directories.Any(directory =>
            directory.Equals(
                "terrain",
                StringComparison.OrdinalIgnoreCase) ||
            directory.Equals(
                "terrains",
                StringComparison.OrdinalIgnoreCase));

        return isTerrain && name.EndsWith(
            "_lm",
            StringComparison.OrdinalIgnoreCase);
    }
}
