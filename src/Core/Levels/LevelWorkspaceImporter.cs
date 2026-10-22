
using System.Text.Json;
using Core.Levels.XRay;

namespace Core.Levels;

public sealed record LevelWorkspaceImportResult(
    string DirectoryPath,
    int FileCount,
    uint CollisionVertices,
    uint CollisionTriangles,
    int VisualMeshes,
    long VisualTriangles,
    string VisualStatus);

public sealed class LevelWorkspaceImporter
{
    private readonly XRayGeometryConverter _geometry = new();
    private readonly XRayFastPathConverter _fastPath = new();
    private readonly XRayLodConverter _lods = new();

    private static readonly string[] EditableDirectories =
    [
        "vegetation",
        "materials",
        "objects",
        "decals"
    ];

    public LevelWorkspaceImportResult Import(string levelName)
    {
        LevelPaths.EnsureDirectories();

        string sourceDirectory =
            LevelPaths.SourceDirectory(levelName);

        string destination =
            LevelPaths.LevelDirectory(levelName);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                sourceDirectory);
        }

        string[] files = Directory
            .EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(file => LevelFilePolicy.Include(
                Path.GetRelativePath(sourceDirectory, file)))
            .OrderBy(
                file => file,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? level = FindSourceFile(files, "level");
        string? geom = FindSourceFile(files, "level.geom");
        string? geomx = FindSourceFile(files, "level.geomx");
        string? cform = FindSourceFile(files, "level.cform");

        if (level is null || geom is null)
        {
            throw new InvalidDataException(
                "Для импорта необходимы level и level.geom.");
        }

        string operationId = Guid.NewGuid().ToString("N");

        string staging =
            destination + ".staging-" + operationId;

        string backup =
            destination + ".backup-" + operationId;

        try
        {
            CreateWorkspaceDirectories(staging);

            var records = new List<LevelFileRecord>();

            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(
                    sourceDirectory,
                    file);

                string target = Path.Combine(
                    staging,
                    "source",
                    relative);

                string? parent = Path.GetDirectoryName(target);

                if (parent is not null)
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(file, target);

                string mode = Path.GetFileName(file).Equals(
                    "level.cform",
                    StringComparison.OrdinalIgnoreCase)
                    ? "converted+preserved"
                    : "preserved";

                records.Add(new LevelFileRecord(
                    relative.Replace('\\', '/'),
                    new FileInfo(file).Length,
                    mode));
            }

            uint collisionVertices = 0;
            uint collisionTriangles = 0;

            string geometryDirectory = Path.Combine(
                staging,
                "geometry");

            if (cform is not null)
            {
                using Stream input = File.OpenRead(cform);

                using Stream output = File.Create(
                    Path.Combine(
                        geometryDirectory,
                        "collision.zmesh"));

                (collisionVertices, collisionTriangles) =
                    CollisionConverter.Convert(input, output);
            }

            VisualConversionResult geometryResult;

            using (Stream output = File.Create(
                       Path.Combine(
                           geometryDirectory,
                           "render.zgeom")))
            using (Stream preview = File.Create(
                       Path.Combine(
                           geometryDirectory,
                           "preview.zmesh")))
            {
                geometryResult = _geometry.Convert(
                    level,
                    geom,
                    output,
                    preview);
            }

            VisualConversionResult? fastPathResult = null;

            if (geomx is not null)
            {
                using Stream output = File.Create(
                    Path.Combine(
                        geometryDirectory,
                        "render-fast.zgeom"));

                fastPathResult = _fastPath.Convert(
                    level,
                    geomx,
                    output);
            }

            int lodCount;

            using (Stream output = File.Create(
                       Path.Combine(
                           geometryDirectory,
                           "lod.zgeom")))
            {
                lodCount = _lods.Convert(
                    level,
                    output);
            }

            string visualStatus = fastPathResult is null
                ? "geometry+lod"
                : "geometry+fastpath+lod";

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
                visualStatus);

            using (Stream output = File.Create(
                       Path.Combine(staging, "scene.json")))
            {
                JsonSerializer.Serialize(
                    output,
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
            }

            PreserveEditableData(
                destination,
                staging);

            CommitWorkspace(
                staging,
                destination,
                backup);

            return new LevelWorkspaceImportResult(
                destination,
                files.Length,
                collisionVertices,
                collisionTriangles,
                geometryResult.Meshes + lodCount,
                geometryResult.Triangles + lodCount * 16L,
                visualStatus);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void CreateWorkspaceDirectories(
        string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "source"));
        Directory.CreateDirectory(Path.Combine(directory, "geometry"));

        foreach (string name in EditableDirectories)
        {
            Directory.CreateDirectory(Path.Combine(directory, name));
        }
    }

    private static void PreserveEditableData(
        string previous,
        string staging)
    {
        if (!Directory.Exists(previous))
        {
            return;
        }

        foreach (string name in EditableDirectories)
        {
            string source = Path.Combine(previous, name);
            string target = Path.Combine(staging, name);

            if (!Directory.Exists(source))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(
                         source,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(
                    source,
                    file);

                string destination = Path.Combine(
                    target,
                    relative);

                string? parent = Path.GetDirectoryName(destination);

                if (parent is not null)
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(
                    file,
                    destination,
                    overwrite: true);
            }
        }
    }

    private static void CommitWorkspace(
        string staging,
        string destination,
        string backup)
    {
        bool previousMoved = false;

        if (Directory.Exists(destination))
        {
            Directory.Move(destination, backup);
            previousMoved = true;
        }

        try
        {
            Directory.Move(staging, destination);
        }
        catch
        {
            if (previousMoved &&
                !Directory.Exists(destination) &&
                Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }

            throw;
        }

        if (previousMoved)
        {
            try
            {
                Directory.Delete(backup, recursive: true);
            }
            catch (IOException)
            {
                // Новая версия готова. Резервную копию
                // можно удалить позднее вручную.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? FindSourceFile(
        IEnumerable<string> files,
        string filename)
    {
        return files.FirstOrDefault(file =>
            Path.GetFileName(file).Equals(
                filename,
                StringComparison.OrdinalIgnoreCase));
    }
}
