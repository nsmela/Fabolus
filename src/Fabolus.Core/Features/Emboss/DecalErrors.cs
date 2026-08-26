using Fabolus.Core.Common;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Centralized registry of decal domain errors.
/// </summary>
public static class DecalErrors
{
    public static readonly Error NoDecalsProvided = new("Decal.NoDecals", "No decals were provided to apply.");
    public static readonly Error EmptyOutlines = new("Decal.EmptyOutlines", "No outline contours provided to build text mesh.");
    public static readonly Error MissingOutlineSource = new("Decal.MissingOutlineSource", "No glyph outline provider configured.");
    public static readonly Error MissingTargetMesh = new("Decal.MissingTargetMesh", "Target mesh is required but was not found.");
    public static readonly Error TriangulationFailed = new("Decal.TriangulationFailed", "Planar triangulation failed for text outlines.");
    public static readonly Error BooleanOperationFailed = new("Decal.BooleanFailed", "Boolean operation failed during decal application.");
    public static readonly Error RaycastFailed = new("Decal.RaycastFailed", "Failed to project decal onto surface.");
}
