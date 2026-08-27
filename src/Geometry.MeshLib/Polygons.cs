using System.Numerics;
using Clipper2Lib;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryMeshLib;

/// <summary>
/// 2D polygon operations: projecting meshes to outlines, offsetting and unioning those
/// outlines, and extruding them back into meshes. Split out of <see cref="GeometryGenerators"/>
/// so the polygon pipeline the mould feature runs on is addressable on its own.
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

        using var model = mesh.ToMRMesh();
        using var validVerts = model.topology.getValidVerts();
        var pts = model.points.vec;

        NetTopologySuite.Geometries.GeometryFactory factory = new();
        var ntsPts = new List<NetTopologySuite.Geometries.Point>();
        
        for (ulong i = 0; i < pts.size(); i++)
        {
            var vid = new MR.VertId((int)i);
            if (validVerts.test(vid))
            {
                var pt = pts[i];
                ntsPts.Add(new NetTopologySuite.Geometries.Point(pt.x, pt.y));
            }
        }

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

        using var model = mesh.ToMRMesh();
        using var validVerts = model.topology.getValidVerts();
        var pts = model.points.vec;

        NetTopologySuite.Geometries.GeometryFactory factory = new();
        var ntsPts = new List<NetTopologySuite.Geometries.Point>();

        for (ulong i = 0; i < pts.size(); i++)
        {
            var vid = new MR.VertId((int)i);
            if (validVerts.test(vid))
            {
                var pt = pts[i];
                ntsPts.Add(new NetTopologySuite.Geometries.Point(pt.x, pt.y));
            }
        }

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

        var solution = Clipper.Union(subjects, FillRule.NonZero);
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
        using var contours = new MR.Std.Vector_StdVectorMRVector2f();
        using var contour = new MR.Std.Vector_MRVector2f();
        foreach (var pt in polygon.OuterBoundary)
        {
            contour.pushBack(new MR.Vector2f(pt.X, pt.Y));
        }
        contour.pushBack(new MR.Vector2f(polygon.OuterBoundary[0].X, polygon.OuterBoundary[0].Y));
        contours.pushBack(contour);

        // Each hole is its own closed contour; triangulateContours treats a contour nested
        // inside another as a hole to cut out (this was previously dropped - polygon.Holes
        // was never read - so extruding a Polygon2D with holes silently ignored them).
        foreach (var hole in polygon.Holes)
        {
            using var holeContour = new MR.Std.Vector_MRVector2f();
            foreach (var pt in hole)
            {
                holeContour.pushBack(new MR.Vector2f(pt.X, pt.Y));
            }
            holeContour.pushBack(new MR.Vector2f(hole[0].X, hole[0].Y));
            contours.pushBack(holeContour);
        }

        var polyMesh = MR.PlanarTriangulation.triangulateContours(contours, null);
        if (polyMesh is null || polyMesh.topology.getValidFaces().count() == 0)
            return new Error("Geometry.TriangulationFailed", "Failed to triangulate buffered path.");

        ulong ptsCount = polyMesh.points.vec.size();
        var bottomMap = new int[ptsCount];
        var topMap = new int[ptsCount];

        var pVerts = polyMesh.topology.getValidVerts();
        var pPts = polyMesh.points.vec;

        var vertices = new List<double>();
        var triangles = new List<int>();

        for (ulong i = 0; i < ptsCount; i++)
        {
            var vid = new MR.VertId((int)i);
            if (!pVerts.test(vid)) continue;

            var v = pPts[i];
            
            int bottomIdx = vertices.Count / 3;
            vertices.Add(v.x);
            vertices.Add(v.y);
            vertices.Add(zMin);
            bottomMap[i] = bottomIdx;

            int topIdx = vertices.Count / 3;
            vertices.Add(v.x);
            vertices.Add(v.y);
            vertices.Add(zMax);
            topMap[i] = topIdx;
        }

        var pFaces = polyMesh.topology.getValidFaces();
        ulong pFaceCap = polyMesh.topology.faceCapacity();
        
        var edgeCounts = new Dictionary<(int, int), int>();

        for (ulong i = 0; i < pFaceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (!pFaces.test(fid)) continue;

            var tri = polyMesh.topology.getTriVerts(fid);
            int a = tri.elems._0.get();
            int b = tri.elems._1.get();
            int c = tri.elems._2.get();

            var vA = pPts[(ulong)a];
            var vB = pPts[(ulong)b];
            var vC = pPts[(ulong)c];
            
            // Check 2D winding (Cross product Z-component)
            double cross = (vB.x - vA.x) * (vC.y - vA.y) - (vB.y - vA.y) * (vC.x - vA.x);
            if (cross < 0)
            {
                int temp = b; b = c; c = temp;
            }

            // Bottom face (normal points down, so CW from top view)
            triangles.Add(bottomMap[a]); triangles.Add(bottomMap[c]); triangles.Add(bottomMap[b]);
            
            // Top face (normal points up, so CCW from top view)
            triangles.Add(topMap[a]); triangles.Add(topMap[b]); triangles.Add(topMap[c]);
            
            // Track directed edges
            EdgeCounter.Add(edgeCounts, a, b);
            EdgeCounter.Add(edgeCounts, b, c);
            EdgeCounter.Add(edgeCounts, c, a);
        }

        // Side walls from boundary edges
        foreach (var kvp in edgeCounts)
        {
            var (a, b) = kvp.Key;
            if (!edgeCounts.ContainsKey((b, a)))
            {
                // Side wall (normal points outward to the right of a->b)
                triangles.Add(bottomMap[a]); triangles.Add(bottomMap[b]); triangles.Add(topMap[b]);
                triangles.Add(bottomMap[a]); triangles.Add(topMap[b]); triangles.Add(topMap[a]);
            }
        }

        var meshResult = _engine.CreateMesh(
            vertices.ToArray().AsSpan(),
            triangles.ToArray().AsSpan());

        if (meshResult.IsFailure)
            return meshResult;

        var finalMesh = meshResult.Value;

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "Extruded Mould")
             .Set(CoreKeys.CreatedBy, "ExtrudePolygon"));

        return Result.Success(finalMesh.WithMetadata(metadata));
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
