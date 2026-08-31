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

    public static readonly Error UnknownRotationAxis = new("Transform.UnknownAxis", "The specified rotation axis is not recognized.");

    // ===== RAYCAST =====
    public static readonly Error RaycastMiss = new("Mesh.RaycastMiss", "The ray did not intersect with the mesh.");

    public static readonly Error NotImplemented = new("Feature.NotImplemented", "This feature is not yet implemented.");

    // ===== PARTING LINE / SPLIT =====
    public static readonly Error InvalidPullDirection = new("PartingLine.InvalidDirection", "Pull direction cannot be zero.");
    public static readonly Error NoPartingLineDetected = new("PartingLine.NoneDetected", "No parting line could be found for this direction - the mesh may not have a silhouette crossing along it.");
    public static readonly Error InvalidPartingLine = new("PartingLine.Invalid", "The parting line is empty or has degenerate loops.");
    public static readonly Error SplitToolGenerationFailed = new("Split.ToolGenerationFailed", "Failed to build the parting tool solid.");
    public static readonly Error SplitProducedSinglePiece = new("Split.SinglePiece", "The parting mesh did not separate the mould into two halves - it may not fully cross the mould along this direction.");

    // ===== FLANGE INPUT GUARDS =====
    // The flange builder offsets the parting loop outward ring by ring and triangulates the result.
    // Feeding it a loop that crosses itself, or one whose footprint has collapsed, makes that work
    // grow without bound - observed spinning for 20+ minutes on a self-intersecting loop and taking
    // the process down on a fragmented one. These reject such input up front, cheaply, so the caller
    // gets a failure it can report rather than a hang it cannot.
    public static readonly Error FlangeLoopSelfIntersects = new("Flange.LoopSelfIntersects", "The parting line crosses itself when viewed along the pull direction, so the flange cannot be offset outward from it. Try a different pull direction or more smoothing.");
    public static readonly Error FlangeLoopDegenerate = new("Flange.LoopDegenerate", "The parting line encloses no area when viewed along the pull direction.");
    public static readonly Error FlangeLoopNotFinite = new("Flange.LoopNotFinite", "The parting line contains non-finite coordinates.");
    public static readonly Error FlangeBudgetExceeded = new("Flange.BudgetExceeded", "Building the parting flange exceeded its time budget and was stopped. The parting line is likely too convoluted for this pull direction.");

    // ===== VALIDATION HELPER =====
    public static Error ValidationFailed(string fileName, string reason) =>
        new("Import.ValidationError", $"Mesh '{fileName}' failed validation: {reason}");

}
