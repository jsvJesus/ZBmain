
using System.IO.Compression;
using System.Text.Json;

namespace Core.Levels;

public sealed class LevelArchiveImporter
{
    public LevelImportResult Import(string levelName)
    {
        LevelPaths.EnsureDirectories();

        string target = LevelPaths.PackagePath(levelName);
        string sourceDirectory = Path.Combine(
            LevelPaths.ImportDirectory,
            levelName);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                sourceDirectory);
        }

        string[] inputFiles = Directory.EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(path => LevelFilePolicy.Include(
                Path.GetRelativePath(sourceDirectory, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!inputFiles.Any(path =>
                Path.GetFileName(path).Equals(
                    "level",
                    StringComparison.OrdinalIgnoreCase)) &&
            !inputFiles.Any(path =>
                Path.GetFileName(path).Equals(
                    "level.geom",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Не найден основной файл level или level.geom.");
        }

        string temporaryFile =
            target + "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            var records = new List<LevelFileRecord>();

            uint vertices = 0;
            uint triangles = 0;

            using (var stream = new FileStream(
                       temporaryFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create,
                       leaveOpen: false))
            {
                foreach (string inputFile in inputFiles)
                {
                    string relative = Path.GetRelativePath(
                            sourceDirectory,
                            inputFile)
                        .Replace('\\', '/');

                    string entryName = "source/" + relative;

                    ZipArchiveEntry entry =
                        archive.CreateEntry(
                            entryName,
                            CompressionLevel.NoCompression);

                    using (Stream input = File.OpenRead(inputFile))
                    using (Stream output = entry.Open())
                    {
                        input.CopyTo(output);
                    }

                    string mode = "preserved";

                    if (relative.Equals(
                            "level.cform",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ZipArchiveEntry geometry =
                            archive.CreateEntry(
                                "geometry/collision.zmesh",
                                CompressionLevel.NoCompression);

                        using Stream source =
                            File.OpenRead(inputFile);

                        using Stream destination =
                            geometry.Open();

                        (vertices, triangles) =
                            CollisionConverter.Convert(
                                source,
                                destination);

                        mode = "converted+preserved";
                    }

                    records.Add(new LevelFileRecord(
                        relative,
                        new FileInfo(inputFile).Length,
                        mode));
                }

                var manifest = new LevelManifest(
                    1,
                    levelName,
                    DateTime.UtcNow,
                    records,
                    vertices,
                    triangles);

                ZipArchiveEntry scene =
                    archive.CreateEntry(
                        "scene.json",
                        CompressionLevel.Optimal);

                using Stream sceneStream = scene.Open();

                JsonSerializer.Serialize(
                    sceneStream,
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
            }

            File.Move(
                temporaryFile,
                target,
                overwrite: true);

            return new LevelImportResult(
                target,
                inputFiles.Length,
                vertices,
                triangles);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }
}
