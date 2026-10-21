using System.IO;

namespace Core.Storage;

public sealed class PackCatalog
{
    public IReadOnlyList<PackEntry> GetTopLevelEntries()
    {
        ProjectPaths.EnsurePacksDirectory();

        return Directory.EnumerateFileSystemEntries(ProjectPaths.PacksDirectory)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                ".gitkeep",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new PackEntry(
                Path.GetFileName(path),
                path,
                Directory.Exists(path)))
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
