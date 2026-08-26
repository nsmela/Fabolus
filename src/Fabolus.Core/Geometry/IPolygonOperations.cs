using Fabolus.Core.Common;
using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// 2D polygon operations. Meshes reduce to outlines here, outlines are offset, buffered and
/// unioned here, and outlines extrude back into meshes here - the pipeline the mould and decal
/// features are built on. Kept apart from <see cref="IGeometryGenerators"/> because these take
/// and return <see cref="Polygon2D"/> rather than producing procedural geometry.
/// </summary>
public interface IPolygonOperations
{
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
    /// Mirrors a 2D polygon across the X-axis, preserving outer/hole winding.
    /// </summary>
    Polygon2D MirrorX(Polygon2D polygon);
}
