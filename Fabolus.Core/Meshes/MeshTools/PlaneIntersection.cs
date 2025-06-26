using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.PolygonTools;
using g3;
using System.Windows.Shapes;
using static Fabolus.Core.Meshes.PolygonTools.PolygonTools;
using static MR.DotNet;
using EdgeLoop = g3.EdgeLoop;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static class Contouring {
        const float CONVEX_MAX_EDGE_LENGTH = 4.0f;
        const float CONVEX_MIN_EDGE_LENGTH = 1.0f;
        const float CURVE_SMOOTH_RATE = 0.1f;
        const int CURVE_SMOOTH_ITERS = 5;
        const int CURVE_RESAMPLE_ITERS = 5;
        const double EPSILON = 0.01;

        public record struct Contour(Loop[] Loops);
        public record struct Loop(Vector3d[] Points);

        public static Contour ContourMesh(DMesh3 mesh, double z_height, bool hulled_contour = false) {
            var sliced_mesh = new DMesh3(mesh);

            List<Loop> loops = new();
            foreach (var edgeLoop in CutMesh(sliced_mesh, z_height)) {
                if (edgeLoop.Vertices.Length < 3) { continue; }// 3 point minimum to loop
                Loop loop = new(edgeLoop.Vertices.Select(vId => sliced_mesh.GetVertex(vId)).ToArray());

                if (hulled_contour) { loop = ConvexHullLoop(loop); }
                loops.Add(loop);
            }

            return new Contour(loops.ToArray());
        }

        public static Loop ConvexHullLoop(Loop loop) {
            double height = loop.Points[0].z;
            ConvexHull2 hull = new(loop.Points.Select(v => new Vector2d(v.x, v.y)).ToList(), EPSILON, QueryNumberType.QT_DOUBLE);
            Polygon2d hullPolygon = hull.GetHullPolygon();
            hullPolygon = Resample(hullPolygon);
            return new Loop(hullPolygon.Vertices.Select(v => new Vector3d(v.x, v.y, height)).ToArray());
        }

        public static Contour[] MeshToContours(DMesh3 mesh, double[] slice_heights, bool use_convex_hull = false) {
            if (mesh is null || mesh.TriangleCount == 0) { throw new ArgumentException("Provided mesh is empty."); }

            List<Contour> contours = new();
            foreach (double height in slice_heights) {
                contours.Add(ContourMesh(mesh, height, use_convex_hull));
            }

            return contours.ToArray();
        }

        public static ComparitivePolygon ContourMesh(DMesh3 mesh, DMesh3 tool, double height) {
            if (mesh.IsEmpty()) { throw new ArgumentException("No mesh provided to cut."); }

            return PolygonTools.PolygonTools.ComparativeMeshSlice(mesh, tool, height);
        }

        private static IEnumerable<EdgeLoop> CutMesh(DMesh3 mesh, double z_height) {
            if (mesh.IsEmpty()) { throw new ArgumentException("No mesh provided to cut."); }
            MeshPlaneCut cutter = new(
                mesh,
                new Vector3d(0, 0, z_height),
                Vector3d.AxisZ
            );
            bool successful = cutter.Cut();
            if (!successful) { throw new InvalidOperationException("Failed to contour the mesh at the specified height."); }

            foreach(EdgeLoop edgeLoop in cutter.CutLoops) {
                yield return edgeLoop;
            }
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
    //public static MeshModel PlaneIntersection(MeshModel model, double[] plane_origin, double[] plane_normal) {
    //    const float OFFSET = 1.0f;
    //
    //    float[] min = new float[] {
    //        (float)model.Mesh.CachedBounds.Min.x + OFFSET,
    //        (float)model.Mesh.CachedBounds.Min.y + OFFSET,
    //        (float)model.Mesh.CachedBounds.Min.z,
    //    };
    //
    //    float[] max = new float[] {
    //        (float)model.Mesh.CachedBounds.Max.x + OFFSET,
    //        (float)model.Mesh.CachedBounds.Max.y + OFFSET,
    //        (float)model.Mesh.CachedBounds.Max.z,
    //    };
    //
    //    // create the plane mesh
    //    List<Vector3f> vertices = new() {
    //        new Vector3f(min[0], min[1], min[2]),
    //        new Vector3f(max[0], min[1], min[2]),
    //        new Vector3f(max[0], max[1], min[2]),
    //        new Vector3f(min[0], max[1], min[2]),
    //    };
    //
    //    List<ThreeVertIds> ids = new() {
    //        new ThreeVertIds(0, 1, 2),
    //        new ThreeVertIds(0, 2, 3),
    //    };
    //
    //    Mesh plane = Mesh.FromTriangles(vertices, ids);
    //
    //    // transform the plane to the correct position and orientation
    //    Vector3f origin = new((float)plane_origin[0], (float)plane_origin[1], (float)plane_origin[2]);
    //    Vector3f direction = new((float)plane_normal[0], (float)plane_normal[1], (float)plane_normal[2]);
    //
    //    Vector3f translate = new() { 
    //        X = 0, 
    //        Y = 0, 
    //        Z = origin.Z - min[2] 
    //    };
    //
    //    plane.Transform(new AffineXf3f(translate));
    //
    //    // TODO: rotate the plane to align with the normal
    //
    //    // TODO: create mesh from intersection of plane and mesh
    //    var result = Boolean(model.Mesh.ToMesh(), plane, BooleanOperation.Intersection);
    //
    //    return new MeshModel(result.mesh);
    //}
}
