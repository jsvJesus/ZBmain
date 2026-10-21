namespace Core.Levels;

public sealed record LevelFileRecord(
    string Path,
    long Size,
    string Mode);

public sealed record LevelManifest(
    int FormatVersion,
    string Name,
    DateTime ImportedUtc,
    IReadOnlyList<LevelFileRecord> Files,
    uint CollisionVertices,
    uint CollisionTriangles);

public sealed record LevelImportResult(
    string PackagePath,
    int FileCount,
    uint Vertices,
    uint Triangles);
