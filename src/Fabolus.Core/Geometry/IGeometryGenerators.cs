using Fabolus.Core.Common;
using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Describes the parameters for generating a tube mesh
/// by sweeping a circular cross-section along a polyline path.
/// Based on geometry3sharp's TubeGenerator concept.
/// </summary>
public sealed record TubeParameters
{
    /// <summary>
    /// The ordered path vertices defining the spine of the tube.
    /// Must contain at least two points.
    /// </summary>
    public required IReadOnlyList<Vector3> Path { get; init; }

    /// <summary>
    /// The radius at each path vertex. Must have the same count as Path.
    /// Enables tapered or varying-diameter tubes.
    /// </summary>
    public required IReadOnlyList<float> Radii { get; init; }

    /// <summary>
    /// Number of segments around the circumference of the tube.
    /// Higher values produce smoother tubes at the cost of more triangles.
    /// </summary>
    public int Segments { get; init; } = 16;

    /// <summary>
    /// Whether to cap (close) the ends of the tube.
    /// </summary>
    public bool Capped { get; init; } = true;
}

/// <summary>
/// Describes the parameters for generating an extruded mesh from a 3D path.
/// The path is projected to the XY plane, buffered by Radius, and extruded between ZMin and ZMax.
/// </summary>
public sealed record ExtrudedPathParameters
{
    /// <summary>
    /// The ordered path vertices. Must contain at least two points.
    /// </summary>
    public required IReadOnlyList<Vector3> Path { get; init; }

    /// <summary>
    /// The buffer radius applied to the path.
    /// </summary>
    public required float Radius { get; init; }

    /// <summary>
    /// The minimum Z value of the extrusion (bottom).
    /// </summary>
    public required float ZMin { get; init; }

    /// <summary>
    /// The maximum Z value of the extrusion (top).
    /// </summary>
    public required float ZMax { get; init; }

    /// <summary>
    /// Optional mesh to perform exact surface contouring via Z-raycasts.
    /// </summary>
    public IMesh? TargetMesh { get; init; }
}

/// <summary>
/// Interface for procedural mesh generation operations.
/// </summary>
public interface IGeometryGenerators
{
    /// <summary>
    /// Generates a tube mesh by sweeping a circular cross-section along a path.
    /// </summary>
    Result<IMesh> GenerateTube(TubeParameters parameters);

    /// <summary>
    /// Generates a UV sphere mesh at the given center with the given radius.
    /// </summary>
    Result<IMesh> GenerateSphere(Vector3 center, double radius, int slices = 16);

    /// <summary>
    /// Generates points along an arc with a given bend radius.
    /// Starts at startPoint with startDirection, and ends when the tangent matches endDirection.
    /// </summary>
    IReadOnlyList<Vector3> Arc3d(float bendRadius, Vector3 startPoint, Vector3 startDirection, Vector3 endDirection, int segmentsCount);

    /// <summary>
    /// Generates a mesh by projecting a 3D path to 2D, offsetting it, and extruding vertically.
    /// </summary>
    Result<IMesh> GenerateExtrudedPath(ExtrudedPathParameters parameters);

    /// <summary>
    /// Resamples an open 3D polyline to a uniform spacing and lightly smooths out jitter.
    /// The first and last points are preserved exactly. Paths with fewer than three
    /// points are returned unchanged.
    /// </summary>
    Result<IReadOnlyList<Vector3>> ResampleOpenPath(IReadOnlyList<Vector3> path, float targetSpacing, int smoothingIterations = 2);

    /// <summary>
    /// Gets the exact 2D projection outline (shadow) of the mesh.
    /// </summary>
    Result<Polygon2D> GetMeshShadow(IMesh mesh);

    /// <summary>
    /// Gets the 2D convex hull of the mesh's projection.
    /// </summary>
    Result<Polygon2D> GetConvexHull(IMesh mesh);

    /// <summary>
    /// Offsets a 2D polygon by the specified distance. A negative distance insets it, and
    /// where that splits the polygon into islands, the largest one is returned.
    /// </summary>
    Result<Polygon2D> OffsetPolygon(Polygon2D polygon, float distance);

    /// <summary>
    /// Closes an open 2D polyline into the polygon covering everything within
    /// <paramref name="distance"/> of it. A single-point path buffers into a disc.
    /// </summary>
    Result<Polygon2D> BufferPath(IReadOnlyList<Vector2> path, float distance);

    /// <summary>
    /// Merges overlapping 2D polygons into a single outline. Anything left disjoint is
    /// dropped - the largest resulting contour is the one returned.
    /// </summary>
    Result<Polygon2D> UnionPolygons(IReadOnlyList<Polygon2D> polygons);

    /// <summary>
    /// Extrudes a 2D polygon into a 3D mesh.
    /// </summary>
    Result<IMesh> ExtrudePolygon(Polygon2D polygon, float zMin, float zMax);

    /// <summary>
    /// Builds an extruded 3D solid mesh from 2D polygon outlines in the tangent frame, optionally contouring to a target mesh surface.
    /// </summary>
    Result<IMesh> BuildTextPrism(IReadOnlyList<Polygon2D> outlines, Features.Emboss.DecalFrame frame, float depth, float sink, float overshoot, float maxEdgeLength = 0f, IMesh? targetMesh = null);

    /// <summary>
    /// Projects each vertex of a text prism onto the curved surface of the target mesh along the frame's normal.
    /// </summary>
    Result<IMesh> ProjectTextPrism(IMesh targetMesh, Features.Emboss.DecalFrame frame, IMesh prismMesh, List<string>? warnings = null);
}
