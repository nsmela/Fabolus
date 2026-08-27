using Fabolus.Core.Common;

namespace GeometryMeshLib;

/// <summary>
/// Centralized error registry for the MRGeometryEngine layer.
/// Covers mesh-type mismatches, invalid geometry data, and operation pre-condition failures.
/// </summary>
public static class GeometryErrors
{
    // ===== TYPE GUARD =====
    public static readonly Error InvalidMeshType =
        new("Geometry.InvalidMeshType", "Expected Mesh instance.");

    // ===== NULL / MISSING DATA =====
    public static readonly Error NullMesh =
        new("Geometry.NullMesh", "Mesh cannot be null.");

    public static readonly Error NullMetadata =
        new("Geometry.NullMetadata", "Metadata cannot be null.");

    // ===== VERTEX / TRIANGLE DATA =====
    public static readonly Error InvalidVertexData =
        new("Geometry.InvalidVertexData", "Vertex array length must be divisible by 3.");

    public static readonly Error InvalidTriangleData =
        new("Geometry.InvalidTriangleData", "Triangle array length must be divisible by 3.");

    public static Error InvalidTriangle(int index) =>
        new("Geometry.InvalidTriangle", $"Failed to add triangle at index {index}.");

    // ===== VALIDITY =====
    public static readonly Error InvalidMesh =
        new("Geometry.InvalidMesh", "Mesh has invalid topology.");

    // ===== TRANSFORMS =====
    public static readonly Error InvalidAxis =
        new("Geometry.InvalidAxis", "Rotation axis cannot be zero-length.");

    public static readonly Error InvalidScale =
        new("Geometry.InvalidScale", "Scale factors must be greater than zero.");

    // ===== GENERATORS =====
    public static readonly Error InvalidPath =
        new("Geometry.InvalidPath", "Tube path must contain at least two points.");

    public static readonly Error InvalidRadius =
        new("Geometry.InvalidRadius", "Radius must be greater than zero.");

    public static readonly Error InvalidRadii =
        new("Geometry.InvalidRadii", "Radii count must match the path point count.");

    public static readonly Error InvalidSegments =
        new("Geometry.InvalidSegments", "Segment count must be at least 3.");

    // ===== NOT IMPLEMENTED =====
    public static readonly Error NotImplemented =
        new("Geometry.NotImplemented", "This operation is not yet implemented.");

    public static readonly Error InvalidPolygon =
        new("Geometry.InvalidPolygon", "The polygon is invalid");
}
