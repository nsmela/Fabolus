using g3;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PolygonTools;

public static partial class PolygonTools {
    const double CONCAVE_ALPHA = 0.4f; // Default alpha value for concave hull
    const float CONVEX_MAX_EDGE_LENGTH = 4.0f;
    const float CONVEX_MAX_EDGE_RATIO = 0.2f;
    const float CONVEX_MIN_EDGE_LENGTH = 1.0f;
    const float CURVE_SMOOTH_RATE = 0.1f;
    const int CURVE_SMOOTH_ITERS = 5;
    const int CURVE_RESAMPLE_ITERS = 5;
    const double EPSILON = 0.01;

    // Mesh Errors
    public static readonly MeshError CONVEX_HULL_FAILED = new("Convex hull failed.");
    public static readonly MeshError CONCAVE_HULL_FAILED = new("Concave hull failed.");
    public static readonly MeshError MESH_EMPTY = new("Provided mesh is empty");
    public static readonly MeshError RESAMPLE_FAILED = new("Resampling failed.");

    public static Result<Polygon2d> ConvexContour(DMesh3 model) {
        if (model is null || model.TriangleCount == 0) { return Result<Polygon2d>.Fail(MESH_EMPTY); }

        List<Vector2d> projectedPoints = model.Vertices().Select(v => new Vector2d(v.x, v.y)).ToList();

        ConvexHull2 hull = new(projectedPoints, EPSILON, QueryNumberType.QT_DOUBLE);
        Polygon2d hullPolygon = hull.GetHullPolygon();

        if (hullPolygon.VertexCount == 0) { return Result<Polygon2d>.Fail(CONVEX_HULL_FAILED); }

        hullPolygon = Resample(hullPolygon);
        if (hullPolygon.VertexCount == 0) { return Result<Polygon2d>.Fail(CONVEX_HULL_FAILED); }

        return Result<Polygon2d>.Pass(hullPolygon);
    }

    public static Result<Polygon2d> ConcaveContour(DMesh3 model) {
        if (model is null || model.TriangleCount == 0) { return Result<Polygon2d>.Fail(MESH_EMPTY); }

        GeometryFactory factory = new();

        MultiPoint multiPoint = factory.CreateMultiPoint(
            model.Vertices().Select(v => new Point(v.x, v.y)).ToArray()
        );

        ConcaveHull hull = new(multiPoint) {
            Alpha = CONCAVE_ALPHA,
            MaximumEdgeLength = CONVEX_MAX_EDGE_LENGTH,
            MaximumEdgeLengthRatio = CONVEX_MAX_EDGE_RATIO,
        };

        Geometry result = hull.GetHull();
        if (result is null || result.IsEmpty) { return Result<Polygon2d>.Fail(CONCAVE_HULL_FAILED); }

        var verts = result.Boundary.Coordinates.Select(c => new Vector2d(c.X, c.Y)).Reverse();
        Polygon2d polygon = new(verts);
        polygon = Resample(polygon);
        if (polygon.VertexCount == 0) { return Result<Polygon2d>.Fail(CONVEX_HULL_FAILED); }

        return Result<Polygon2d>.Pass(polygon);
    }

    private static Polygon2d Resample(Polygon2d polygon) {
        DCurve3 hullCurve = new(polygon, 0, 1);
        CurveResampler resampler = new();
        for (int i = 0; i < CURVE_RESAMPLE_ITERS; i++) {
            List<Vector3d> newPoints = resampler.SplitCollapseResample(hullCurve, CONVEX_MAX_EDGE_LENGTH, CONVEX_MIN_EDGE_LENGTH);
            DCurve3 resampledCurve = (newPoints != null) ? new DCurve3(newPoints, true) : hullCurve;
            InPlaceIterativeCurveSmooth smoother = new InPlaceIterativeCurveSmooth(resampledCurve, CURVE_SMOOTH_RATE);
            smoother.UpdateDeformation(CURVE_SMOOTH_ITERS);
            hullCurve = smoother.Curve;
        }

        return new(hullCurve.Vertices.Select(v => new Vector2d(v.x, v.y)));
    }

    public static Result<DMesh3> ExtrudePolygon(Polygon2d polygon, double minHeight, double maxHeight) {
        if (polygon is null || polygon.Vertices.Count < 3) { return Result<DMesh3>.Fail(MESH_EMPTY); }

        // Triangulate the polygon
        TriangulatedPolygonGenerator generator = new() {
            Polygon = new GeneralPolygon2d(polygon),
            Clockwise = false,
        };

        DMesh3 planarMesh = new();
        try {
            planarMesh = generator.Generate().MakeDMesh();
            if (planarMesh is null) {
                return new();
            }
        } catch (Exception ex) {
            return Result<DMesh3>.Fail(new MeshError($"Mesh triangulation failed: {ex.Message}"));
        }

        // Extrude the mesh
        Vector3d z_distance = (maxHeight - minHeight) * Vector3d.AxisZ;
        MeshExtrudeMesh extruder = new(new DMesh3(planarMesh)) {
            ExtrudedPositionF = (Vector3d vert, Vector3f normal, int index) => vert + z_distance,
        };
        bool passed = extruder.Extrude();
        if (!passed) { return Result<DMesh3>.Fail(new MeshError("Polygon extrusion failed.")); }

        MeshTransforms.Translate(extruder.Mesh, new Vector3d(0, 0, minHeight));

        return Result<DMesh3>.Pass(extruder.Mesh);
    }
}
