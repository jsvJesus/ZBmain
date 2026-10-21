using System.IO;

namespace Core.Storage;

public static class ProjectPaths
{
    public static string BinDirectory => AppContext.BaseDirectory;

    public static string PacksDirectory => Path.Combine(BinDirectory, "packs");

    public static void EnsurePacksDirectory()
    {
        Directory.CreateDirectory(PacksDirectory);
    }
}
