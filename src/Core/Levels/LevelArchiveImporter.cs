using System.IO.Compression;
using System.Text.Json;
using Core.Levels.XRay;

namespace Core.Levels;

public sealed class LevelArchiveImporter
{
    private readonly XRayGeometryConverter _geometry = new();
    private readonly XRayFastPathConverter _fastPath = new();
    private readonly XRayLodConverter _lods = new();

    public LevelImportResult Import(string levelName)
    {
        LevelPaths.EnsureDirectories();

        string sourceDirectory = Path.Combine(
            LevelPaths.ImportDirectory,
            levelName);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                sourceDirectory);
        }

        string[] files = Directory.EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(path => LevelFilePolicy.Include(
                Path.GetRelativePath(sourceDirectory, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? level = FindFile(files, "level");
        string? geom = FindFile(files, "level.geom");
        string? geomx = FindFile(files, "level.geomx");

        if (level is null || geom is null)
        {
            throw new InvalidDataException(
                "Для конвертации необходимы level и level.geom.");
        }

        string destination = LevelPaths.PackagePath(levelName);

        string temporaryFile =
            destination + "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            var records = new List<LevelFileRecord>();

            uint collisionVertices = 0;
            uint collisionTriangles = 0;

            VisualConversionResult? geometryResult = null;
            VisualConversionResult? fastPathResult = null;

            int lodCount = 0;

            using (var stream = new FileStream(
                       temporaryFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create))
            {
                foreach (string sourceFile in files)
                {
                    string relative = Path.GetRelativePath(
                            sourceDirectory,
                            sourceFile)
                        .Replace('\\', '/');

                    ZipArchiveEntry entry = archive.CreateEntry(
                        "source/" + relative,
                        CompressionLevel.NoCompression);

                    using (Stream input = File.OpenRead(sourceFile))
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

                        using Stream input = File.OpenRead(sourceFile);
                        using Stream output = collision.Open();

                        (collisionVertices, collisionTriangles) =
                            CollisionConverter.Convert(input, output);

                        mode = "converted+preserved";
                    }

                    records.Add(new LevelFileRecord(
                        relative,
                        new FileInfo(sourceFile).Length,
                        mode));
                }

                using var previewBuffer = new MemoryStream();

                ZipArchiveEntry render = archive.CreateEntry(
                    "geometry/render.zgeom",
                    CompressionLevel.NoCompression);

                using (Stream output = render.Open())
                {
                    geometryResult = _geometry.Convert(
                        level,
                        geom,
                        output,
                        previewBuffer);
                }

                ZipArchiveEntry preview = archive.CreateEntry(
                    "geometry/preview.zmesh",
                    CompressionLevel.NoCompression);

                previewBuffer.Position = 0;

                using (Stream output = preview.Open())
                {
                    previewBuffer.CopyTo(output);
                }

                if (geomx is not null)
                {
                    ZipArchiveEntry fast = archive.CreateEntry(
                        "geometry/render-fast.zgeom",
                        CompressionLevel.NoCompression);

                    using Stream output = fast.Open();

                    fastPathResult = _fastPath.Convert(
                        level,
                        geomx,
                        output);
                }

                ZipArchiveEntry lod = archive.CreateEntry(
                    "geometry/lod.zgeom",
                    CompressionLevel.NoCompression);

                using (Stream output = lod.Open())
                {
                    lodCount = _lods.Convert(
                        level,
                        output);
                }

                var manifest = new LevelManifest(
                    3,
                    levelName,
                    DateTime.UtcNow,
                    records,
                    collisionVertices,
                    collisionTriangles,
                    geometryResult.Meshes + lodCount,
                    geometryResult.Triangles + lodCount * 16L,
                    geometryResult.Materials,
                    fastPathResult is null
                        ? "geometry+lod"
                        : "geometry+fastpath+lod");

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
                destination,
                overwrite: true);

            return new LevelImportResult(
                destination,
                files.Length,
                collisionVertices,
                collisionTriangles,
                geometryResult!.Meshes + lodCount,
                geometryResult.Triangles + lodCount * 16L,
                fastPathResult is null
                    ? "geometry+lod"
                    : "geometry+fastpath+lod");
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    private static string? FindFile(
        IEnumerable<string> files,
        string filename)
    {
        return files.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(
                filename,
                StringComparison.OrdinalIgnoreCase));
    }
}
