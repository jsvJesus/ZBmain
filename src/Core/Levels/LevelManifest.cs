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
    uint CollisionTriangles,
    int VisualMeshes = 0,
    long VisualTriangles = 0,
    int Materials = 0,
    string VisualStatus = "not-converted");

public sealed record LevelImportResult(
    string PackagePath,
    int FileCount,
    uint Vertices,
    uint Triangles,
    int VisualMeshes = 0,
    long VisualTriangles = 0,
    string VisualStatus = "not-converted");
