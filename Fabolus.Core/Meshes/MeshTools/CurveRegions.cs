using g3;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Core.Meshes.MeshTools.DraftRegions;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {

    public enum CurveRegionClassification {
        None,
        Flat,
        Valley,
        Ridge,
    }

    public static Dictionary<CurveRegionClassification, MeshModel> GenerateCurveRegions(MeshModel model) {
        DMesh3 mesh = model.Mesh;
        double[] curves = new double[mesh.EdgeCount];
        CurveRegionClassification[] classifications = new CurveRegionClassification[mesh.TriangleCount];

        foreach (int eId in mesh.EdgeIndices()) {
            curves[eId] = MeshTools.Curvature(mesh, eId);// .GuassianCurvature(mesh, vId);
        }

        foreach(int tId in mesh.TriangleIndices()) {
            Index3i tri = mesh.GetTriangle(tId);
            var t = mesh.GetTriEdges(tId);

            // average the curves of the triangle's vertices
            double curve_sum = 0;
            curve_sum += curves[tri.a];
            curve_sum += curves[tri.b];
            curve_sum += curves[tri.c];
            curve_sum /= 3;

            classifications[tId] = curve_sum switch {
                > 0.5 => CurveRegionClassification.Ridge,
                < -0.5 => CurveRegionClassification.Valley,
                _ => CurveRegionClassification.Flat,
            };
        }

        // create a mesh for each classification
        DMesh3 none_mesh = new DMesh3();
        DMesh3 flat_mesh = new DMesh3();
        DMesh3 valley_mesh = new DMesh3();
        DMesh3 ridge_mesh = new DMesh3();

        // easier to ensure the mesh has all the vertices than to feed only the required ones
        // keeps triangle indices references consistent
        foreach (Vector3d v in mesh.Vertices()) {
            none_mesh.AppendVertex(v);
            flat_mesh.AppendVertex(v);
            valley_mesh.AppendVertex(v);
            ridge_mesh.AppendVertex(v);
        }

        // generate meshes for each draft region type
        for (int i = 0; i < mesh.TriangleCount; i++) {

            switch (classifications[i]) {
                case CurveRegionClassification.Flat:
                    flat_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
                case CurveRegionClassification.Valley:
                    valley_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
                case CurveRegionClassification.Ridge:
                    ridge_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
                default:
                    none_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
            }
        }

        return new Dictionary<CurveRegionClassification, MeshModel> {
            { CurveRegionClassification.None, new MeshModel(none_mesh) },
            { CurveRegionClassification.Flat, new MeshModel(flat_mesh) },
            { CurveRegionClassification.Valley, new MeshModel(valley_mesh) },
            { CurveRegionClassification.Ridge, new MeshModel(ridge_mesh) },
        };

    }

    // ref: https://computergraphics.stackexchange.com/questions/1718/what-is-the-simplest-way-to-compute-principal-curvature-for-a-mesh-triangle
    private static double Curvature(DMesh3 mesh, int eId) {
        Index2i edge = mesh.GetEdgeV(eId);

        Vector3d p1 = mesh.GetVertex(edge.a);
        Vector3d p2 = mesh.GetVertex(edge.b);

        Vector3d n1 = mesh.GetVertexNormal(edge.a);
        Vector3d n2 = mesh.GetVertexNormal(edge.b);

        var n = n2 - n1;
        var p = p2 - p1;
        var product = n * p;
        var abs_sq = p.Abs * p.Abs;
        return (product / abs_sq).MaxAbs;
    }

    // ref: https://rodolphe-vaillant.fr/entry/33/curvature-of-a-triangle-mesh-definition-and-computation
    private static double GuassianCurvature(DMesh3 mesh, int vId) {
        double angle_sum = 0;
        double triangle_area = 0;
        foreach (int tId in mesh.VtxTrianglesItr(vId)) {
            Index3i tri_verts = mesh.GetTriangle(tId);
            angle_sum += mesh.GetTriInternalAngleR(tId, vId);
            triangle_area += mesh.GetTriArea(tId);
        }

        triangle_area /= 3; // barycentric cell area is one third of the sum of the triangles' area adjacent to the vertex

        return (MathUtil.TwoPI - angle_sum) / triangle_area;
    }

    private static double MeanCurvature(DMesh3 mesh, int vId) {

        return 0.0;
    }
}
