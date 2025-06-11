using g3;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Simplify;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    const double CONCAVE_ALPHA = 0.4f; // Default alpha value for concave hull
    const float CONVEX_MAX_EDGE_LENGTH = 4.0f;
    const float CONVEX_MAX_EDGE_RATIO = 0.2f;
    const float CONVEX_MIN_EDGE_LENGTH = 1.0f;
    const float CURVE_SMOOTH_RATE = 0.1f;
    const int CURVE_SMOOTH_ITERS = 5;
    const int CURVE_RESAMPLE_ITERS = 5;
    const double EPSILON = 0.01;

    public static Polygon2d ConvexContour(DMesh3 model) {
        if (model is null || model.TriangleCount == 0) { return new(); }

        List<Vector2d> projectedPoints = model.Vertices().Select(v => new Vector2d(v.x, v.y)).ToList();

        ConvexHull2 hull = new(projectedPoints, EPSILON, QueryNumberType.QT_DOUBLE);
        Polygon2d hullPolygon = hull.GetHullPolygon();

        return Resample(hullPolygon);
    }

    public static Polygon2d ConcaveContour(DMesh3 model) {
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
        if (result is null || result.IsEmpty) {
            return new();
        }

        var verts = result.Boundary.Coordinates.Select(c => new Vector2d(c.X, c.Y)).Reverse();
        Polygon2d polygon = new(verts);

        return Resample(polygon);
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

}


