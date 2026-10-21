
using System.IO.Compression;
using System.Text.Json;
using Core.Levels.XRay;

namespace Core.Levels;

public sealed class LevelArchiveImporter
{
    private readonly XRayGeometryConverter _visualConverter = new();

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

        string? levelFile = inputFiles.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(
                "level",
                StringComparison.OrdinalIgnoreCase));

        string? geomFile = inputFiles.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(
                "level.geom",
                StringComparison.OrdinalIgnoreCase));

        if (levelFile is null && geomFile is null)
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

            uint collisionVertices = 0;
            uint collisionTriangles = 0;

            VisualConversionResult? visuals = null;
            string visualStatus = "missing level or level.geom";

            using (var stream = new FileStream(
                       temporaryFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create))
            {
                foreach (string inputFile in inputFiles)
                {
                    string relative = Path.GetRelativePath(
                            sourceDirectory,
                            inputFile)
                        .Replace('\\', '/');

                    ZipArchiveEntry entry = archive.CreateEntry(
                        "source/" + relative,
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
                        ZipArchiveEntry collision = archive.CreateEntry(
                            "geometry/collision.zmesh",
                            CompressionLevel.NoCompression);

                        using Stream source = File.OpenRead(inputFile);
                        using Stream destination = collision.Open();

                        (collisionVertices, collisionTriangles) =
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

                if (levelFile is not null && geomFile is not null)
                {
                    using var previewBuffer = new MemoryStream();

                    ZipArchiveEntry renderGeometry = archive.CreateEntry(
                        "geometry/render.zgeom",
                        CompressionLevel.NoCompression);

                    using (Stream geometryStream = renderGeometry.Open())
                    {
                        visuals = _visualConverter.Convert(
                            levelFile,
                            geomFile,
                            geometryStream,
                            previewBuffer);
                    }

                    ZipArchiveEntry renderPreview = archive.CreateEntry(
                        "geometry/preview.zmesh",
                        CompressionLevel.NoCompression);

                    previewBuffer.Position = 0;

                    using (Stream previewStream = renderPreview.Open())
                    {
                        previewBuffer.CopyTo(previewStream);
                    }

                    visualStatus = "converted-xray14";
                }

                var manifest = new LevelManifest(
                    2,
                    levelName,
                    DateTime.UtcNow,
                    records,
                    collisionVertices,
                    collisionTriangles,
                    visuals?.Meshes ?? 0,
                    visuals?.Triangles ?? 0,
                    visuals?.Materials ?? 0,
                    visualStatus);

                ZipArchiveEntry scene = archive.CreateEntry(
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
                collisionVertices,
                collisionTriangles,
                visuals?.Meshes ?? 0,
                visuals?.Triangles ?? 0,
                visualStatus);
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
