using Fabolus.Core.Common;

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

    // ===== TRANSFORM =====
    public static readonly Error UnknownRotationAxis = new("Transform.UnknownAxis", "The specified rotation axis is not recognized.");

    public static readonly Error NotImplemented = new("Feature.NotImplemented", "This feature is not yet implemented.");

    // ===== PARTING LINE / SPLIT =====
    public static readonly Error InvalidPullDirection = new("PartingLine.InvalidDirection", "Pull direction cannot be zero.");
    public static readonly Error NoPartingLineDetected = new("PartingLine.NoneDetected", "No parting line could be found for this direction - the mesh may not have a silhouette crossing along it.");
    public static readonly Error InvalidPartingLine = new("PartingLine.Invalid", "The parting line is empty or has degenerate loops.");
    public static readonly Error SplitToolGenerationFailed = new("Split.ToolGenerationFailed", "Failed to build the parting tool solid.");

    // ===== VALIDATION HELPER =====
    public static Error ValidationFailed(string fileName, string reason) =>
        new("Import.ValidationError", $"Mesh '{fileName}' failed validation: {reason}");

}
