using BasicResults;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Centralized registry of mesh domain errors.
/// </summary>
public static class MeshErrors
{
    // ===== VALIDATION =====
    public static readonly Error NullSource = new("Mesh.Null", "The source mesh cannot be null.");
    public static readonly Error CorruptTopology = new("Mesh.Corrupt", "The mesh has corrupt internal topology.");
    public static readonly Error NotWatertight = new("Mesh.NotWatertight", "Mesh must be watertight (closed).");
    public static readonly Error OrphanedVertices = new("Mesh.OrphanedVertices", "The mesh contains orphaned vertices.");
    public static readonly Error DegenerateTriangles = new("Mesh.DegenerateTriangles", "The mesh contains zero-area triangles.");

    // ===== EXPORT =====
    public static readonly Error ExportMeshIsNull = new("Export.MeshIsNull", "Mesh cannot be null.");
    public static readonly Error ExportFilePathIsEmpty = new("Export.FilePathIsEmpty", "File path cannot be null or empty.");
    public static readonly Error ExportRecordIsNull = new("Export.RecordIsNull", "The workspace entry cannot be null.");

    public static Error ExportFileExists(string filePath) =>
        new("Export.FileExists", $"'{filePath}' already exists. Pass overwrite to replace it.");

    public static Error ExportFailed(string detail) =>
        new("Export.Failed", $"The file could not be written: {detail}.");

    // ===== RAYCAST =====
    public static readonly Error RaycastMiss = new("Mesh.RaycastMiss", "The ray did not intersect with the mesh.");

    public static readonly Error NotImplemented = new("Feature.NotImplemented", "This feature is not yet implemented.");
}
