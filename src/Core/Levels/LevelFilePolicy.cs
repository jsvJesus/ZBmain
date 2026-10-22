
namespace Core.Levels;

public static class LevelFilePolicy
{
    private static readonly string[] ExcludedNames =
    [
        "level.ai",
        "level.graph",
        "level.gct",
        "level.snd",
        "level.sound",
        "level.som",
        "level.light",
        "level.env_mod",
        "level.fog_vol"
    ];

    public static bool Include(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        string name = Path.GetFileName(path);

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string firstDirectory = path.Split('/')[0];

        if (IsExcluded(name))
        {
            return false;
        }

        if (path.Contains('/'))
        {
            return firstDirectory.Equals(
                       "terrain",
                       StringComparison.OrdinalIgnoreCase) ||
                   firstDirectory.Equals(
                       "terrains",
                       StringComparison.OrdinalIgnoreCase) ||
                   firstDirectory.Equals(
                       "meshes",
                       StringComparison.OrdinalIgnoreCase);
        }

        if (name.Equals(
                "level",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith(
                "level.",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                "level_",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals(
                "build_details.dds",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith(
                "lmap#",
                StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(
                ".dds",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsExcluded(string name)
    {
        if (name.StartsWith(
                "build.lights",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string excluded in ExcludedNames)
        {
            if (name.Equals(
                    excluded,
                    StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(
                    excluded + ".",
                    StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(
                    excluded + "_",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
