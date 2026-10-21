namespace Core.Levels;

public static class LevelFilePolicy
{
    private static readonly string[] ExcludedNames =
    [
        "level.ai",
        "level.graph",
        "level.gct",
        "level.snd",
        "level.som",
        "level.light",
        "level.env_mod",
        "level.fog_vol"
    ];

    public static bool Include(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        string name = Path.GetFileName(path);
        string firstDirectory = path.Split('/')[0];

        if (name.StartsWith(
                "build.lights",
                StringComparison.OrdinalIgnoreCase) ||
            ExcludedNames.Any(excluded =>
                name.Equals(
                    excluded,
                    StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(
                    excluded + ".",
                    StringComparison.OrdinalIgnoreCase) ||
                (excluded.EndsWith(
                     "snd",
                     StringComparison.OrdinalIgnoreCase) &&
                 name.StartsWith(
                     excluded + "_",
                     StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        if (path.Contains('/') &&
            (firstDirectory.Equals(
                 "meshes",
                 StringComparison.OrdinalIgnoreCase) ||
             firstDirectory.Equals(
                 "terrain",
                 StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (path.Contains('/'))
        {
            return false;
        }

        return name.Equals(
                   "level",
                   StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(
                   "level.",
                   StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(
                   "level_",
                   StringComparison.OrdinalIgnoreCase);
    }
}
