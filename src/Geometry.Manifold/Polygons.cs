using System.Numerics;
using Clipper2Lib;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal;
using GeometryManifold.Internal.Native;

namespace GeometryManifold;

/// <summary>
/// 2D polygon operations: projecting meshes to outlines, offsetting and unioning those
/// outlines, and extruding them back into meshes. Clipper2 and NetTopologySuite do the planar
/// work here exactly as they did under MeshLib - only the mesh-facing ends changed, and the
/// extrusion now comes back from Manifold as a solid it guarantees is closed.
/// </summary>
internal sealed class Polygons : IPolygonOperations
{
    private readonly GeometryEngine _engine;

    public Polygons(GeometryEngine engine)
    {
        _engine = engine;
    }

    public Result<Polygon2D> GetMeshShadow(IMesh mesh)
    {
        if (mesh is null) return GeometryErrors.InvalidMesh;

        NetTopologySuite.Geometries.GeometryFactory factory = new();
        var ntsPts = ProjectToPlane(mesh);

        var multiPoint = factory.CreateMultiPoint(ntsPts.ToArray());

        var hull = new NetTopologySuite.Algorithm.Hull.ConcaveHull(multiPoint) {
            Alpha = 0.4f,
            MaximumEdgeLength = 4.0f,
            MaximumEdgeLengthRatio = 0.2f,
        };

        var result = hull.GetHull();
        if (result is null || result.IsEmpty) {
            return new Error("Geometry.HullFailed", "Failed to compute concave hull.");
        }

        var verts = result.Boundary.Coordinates.Select(c => new Vector2((float)c.X, (float)c.Y)).Reverse().ToList();
        var polygon2d = new g3.Polygon2d(verts.Select(v => new g3.Vector2d(v.X, v.Y)));
        var resampled = Resample(polygon2d);

        return new Polygon2D { OuterBoundary = resampled.Vertices.Select(v => new Vector2((float)v.x, (float)v.y)).ToList() };
    }

    public Result<Polygon2D> GetConvexHull(IMesh mesh)
    {
        if (mesh is null) return GeometryErrors.InvalidMesh;

        NetTopologySuite.Geometries.GeometryFactory factory = new();
        var ntsPts = ProjectToPlane(mesh);

        var multiPoint = factory.CreateMultiPoint(ntsPts.ToArray());
        var hull = new NetTopologySuite.Algorithm.ConvexHull(multiPoint);
        var result = hull.GetConvexHull();

        if (result is null || result.IsEmpty) {
            return new Error("Geometry.HullFailed", "Failed to compute convex hull.");
        }

        var verts = result.Coordinates.Select(c => new Vector2((float)c.X, (float)c.Y)).ToList();
        if (verts.Count > 1 && verts[0] == verts[^1])
            verts.RemoveAt(verts.Count - 1);

        var polygon2d = new g3.Polygon2d(verts.Select(v => new g3.Vector2d(v.X, v.Y)));
        var resampled = Resample(polygon2d);

        return new Polygon2D { OuterBoundary = resampled.Vertices.Select(v => new Vector2((float)v.x, (float)v.y)).ToList() };
    }

    public Result<Polygon2D> OffsetPolygon(Polygon2D polygon, float distance)
    {
        var paths = new Paths64();
        const double scale = 100000.0;

        var path = new Path64();
        foreach (var pt in polygon.OuterBoundary)
        {
            path.Add(new Point64((long)Math.Round(pt.X * scale), (long)Math.Round(pt.Y * scale)));
        }

        // Clipper offsets a closed path according to its winding, so a positive distance
        // only grows a positively-oriented one. GetMeshShadow and GetConvexHull don't agree
        // on winding, so it gets normalised here - otherwise a negative distance (the mould
        // trough insetting its rim) would grow the polygon on half the inputs.
        if (Clipper.Area(path) < 0)
            path.Reverse();

        paths.Add(path);

        var offsetter = new ClipperOffset();
        offsetter.AddPaths(paths, JoinType.Round, EndType.Polygon);
        var solution = new Paths64();
        offsetter.Execute(distance * scale, solution);

        if (solution.Count == 0)
            return new Error("Geometry.OffsetFailed", "Failed to generate offset polygon.");

        // A large enough inset pinches the polygon into separate islands; the callers only
        // ever want one contour, so keep the biggest.
        var largest = solution.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();

        var finalPoly = new Polygon2D { OuterBoundary = largest.Select(pt => new Vector2((float)(pt.X / scale), (float)(pt.Y / scale))).ToList() };
        return Result.Success(finalPoly);
    }

    public Result<Polygon2D> BufferPath(IReadOnlyList<Vector2> path, float distance)
    {
        if (path is null || path.Count == 0)
            return GeometryErrors.InvalidPath;

        if (distance <= 0)
            return new Error("Geometry.OffsetFailed", "A buffered path needs a positive distance.");

        const double scale = 100000.0;

        var points = path;
        if (points.Count == 1)
        {
            // Clipper needs a segment to sweep the round ends along; a hair of length still
            // rounds off into the disc a single point should buffer to.
            points = new[] { path[0], path[0] + new Vector2(0.01f, 0f) };
        }

        var open = new Path64();
        foreach (var pt in points)
        {
            open.Add(new Point64((long)Math.Round(pt.X * scale), (long)Math.Round(pt.Y * scale)));
        }

        var offsetter = new ClipperOffset();
        offsetter.AddPath(open, JoinType.Round, EndType.Round);
        var solution = new Paths64();
        offsetter.Execute(distance * scale, solution);

        if (solution.Count == 0)
            return new Error("Geometry.OffsetFailed", "Failed to buffer path.");

        // A path that doubles back on itself can enclose an island; only the outer contour
        // matters here.
        var largest = solution.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();

        return Result.Success(new Polygon2D
        {
            OuterBoundary = largest.Select(pt => new Vector2((float)(pt.X / scale), (float)(pt.Y / scale))).ToList()
        });
    }

    public Result<Polygon2D> UnionPolygons(IReadOnlyList<Polygon2D> polygons)
    {
        if (polygons is null || polygons.Count == 0)
            return new Error("Geometry.UnionFailed", "No polygons to union.");

        const double scale = 100000.0;

        var subjects = new Paths64();
        foreach (var polygon in polygons)
        {
            var path = new Path64();
            foreach (var pt in polygon.OuterBoundary)
            {
                path.Add(new Point64((long)Math.Round(pt.X * scale), (long)Math.Round(pt.Y * scale)));
            }

            if (path.Count < 3) continue;

            // The non-zero fill rule cancels overlapping regions wound against each other,
            // so every contour goes in the same way round.
            if (Clipper.Area(path) < 0)
                path.Reverse();

            subjects.Add(path);
        }

        if (subjects.Count == 0)
            return new Error("Geometry.UnionFailed", "No polygons with area to union.");

        var solution = Clipper.Union(subjects, Clipper2Lib.FillRule.NonZero);
        if (solution.Count == 0)
            return new Error("Geometry.UnionFailed", "Failed to union polygons.");

        var largest = solution.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();

        return Result.Success(new Polygon2D
        {
            OuterBoundary = largest.Select(pt => new Vector2((float)(pt.X / scale), (float)(pt.Y / scale))).ToList()
        });
    }

    public Result<IMesh> ExtrudePolygon(Polygon2D polygon, float zMin, float zMax)
    {
        if (polygon.OuterBoundary.Count < 3)
            return new Error("Geometry.TriangulationFailed", "An extruded polygon needs at least three points.");

        if (zMax <= zMin)
            return new Error("Geometry.TriangulationFailed", "The extrusion's top must sit above its bottom.");

        // Manifold turns a polygon into a solid in one call, triangulating the caps itself and
        // closing the walls - which is the whole reason the mould pipeline used to hand-roll caps
        // and side walls off MeshLib's planar triangulator.
        var contours = new List<IReadOnlyList<Vector2>>(1 + polygon.Holes.Count);
        contours.Add(Orient(polygon.OuterBoundary, clockwise: false));
        foreach (var hole in polygon.Holes)
        {
            if (hole.Count >= 3) contours.Add(Orient(hole, clockwise: true));
        }

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "Extruded Mould")
             .Set(CoreKeys.CreatedBy, "ExtrudePolygon"));

        var extruded = ManifoldKernel.Extrude(contours, zMax - zMin, zMin, metadata);

        // The callers here disagree about the failure they expect, and every one of them
        // predates Manifold; keeping the MeshLib-era code means nothing downstream has to change.
        return extruded.IsFailure
            ? new Error("Geometry.TriangulationFailed", extruded.Error.Description)
            : extruded;
    }

    /// <summary>
    /// Winds a contour the way Manifold reads it: counter-clockwise for an outline, clockwise for
    /// a hole. The callers here do not agree on which way round they hand their outlines over -
    /// GetMeshShadow and GetConvexHull return opposite windings - so it is settled explicitly
    /// rather than assumed.
    /// </summary>
    private static IReadOnlyList<Vector2> Orient(IReadOnlyList<Vector2> contour, bool clockwise)
    {
        float area = PolygonTriangulator.SignedArea(contour);
        bool isClockwise = area < 0;
        if (isClockwise == clockwise) return contour;

        var reversed = contour.ToList();
        reversed.Reverse();
        return reversed;
    }

    /// <summary>
    /// Drops each mesh vertex onto the XY plane, merging the ones that land on top of each other.
    /// </summary>
    /// <remarks>
    /// A closed mesh has a front face and a back face over the same footprint, so projecting it
    /// hands the same XY to many vertices - and to NetTopologySuite's incremental Delaunay those
    /// near-coincident sites are a degenerate subdivision it throws "Locate failed to converge"
    /// on. MeshLib fed the hull the same points and hit the same wall; snapping to a grid
    /// finer than any real feature and de-duplicating is what keeps the hull solvable.
    /// </remarks>
    private static List<NetTopologySuite.Geometries.Point> ProjectToPlane(IMesh mesh)
    {
        const double snap = 1e-4; // A tenth of a micron: far below print resolution.

        var seen = new HashSet<(long, long)>(mesh.VertexCount);
        var points = new List<NetTopologySuite.Geometries.Point>(mesh.VertexCount);

        foreach (var vertex in mesh.Vertices)
        {
            var key = ((long)Math.Round(vertex.X / snap), (long)Math.Round(vertex.Y / snap));
            if (!seen.Add(key)) continue;

            points.Add(new NetTopologySuite.Geometries.Point(key.Item1 * snap, key.Item2 * snap));
        }

        return points;
    }

    private static g3.Polygon2d Resample(g3.Polygon2d polygon) {
        var pts3d = polygon.Vertices.Select(v => new g3.Vector3d(v.x, v.y, 0)).ToList();
        g3.DCurve3 hullCurve = new(pts3d, true);
        g3.CurveResampler resampler = new();
        for (int i = 0; i < 4; i++) {
            List<g3.Vector3d> newPoints = resampler.SplitCollapseResample(hullCurve, 4.0f, 1.0f);
            g3.DCurve3 resampledCurve = (newPoints is not null) ? new g3.DCurve3(newPoints, true) : hullCurve;
            g3.InPlaceIterativeCurveSmooth smoother = new g3.InPlaceIterativeCurveSmooth(resampledCurve, 0.1f);
            smoother.UpdateDeformation(4);
            hullCurve = smoother.Curve;
        }

        return new g3.Polygon2d(hullCurve.Vertices.Select(v => new g3.Vector2d(v.x, v.y)));
    }

    /// <summary>
    /// Mirrors a 2D polygon across the X-axis ((x, y) -> (-x, y)) and reverses winding so outer
    /// boundaries stay positively-oriented and holes stay negatively-oriented.
    /// </summary>
    public Polygon2D MirrorX(Polygon2D polygon)
    {
        var mirroredOuter = polygon.OuterBoundary
            .Select(v => new Vector2(-v.X, v.Y))
            .Reverse()
            .ToList();

        var mirroredHoles = polygon.Holes
            .Select(hole => (IReadOnlyList<Vector2>)hole.Select(v => new Vector2(-v.X, v.Y)).Reverse().ToList())
            .ToList();

        return new Polygon2D
        {
            OuterBoundary = mirroredOuter,
            Holes = mirroredHoles
        };
    }
}
