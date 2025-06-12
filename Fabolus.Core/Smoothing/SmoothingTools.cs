using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Mould.Builders;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TriangleNet.Geometry;
using TriangleNet.Meshing;

namespace Fabolus.Core.Smoothing;

public static class SmoothingTools {
    //References:
    //https://www.gradientspace.com/tutorials/2018/9/14/point-set-fast-winding

    public static MeshModel Contour(MeshModel model, double z_height) {
        MeshPlaneCut cutter = new(
            model.Mesh, 
            new Vector3d(0, 0, z_height), 
            new Vector3d(0, 0, 1)
        );

        bool successful = cutter.Cut();

        if (!successful) { return new MeshModel(); }
        if (cutter.CutLoops.Count() == 0) { return new MeshModel(); }

        DMesh3 result = new();
        foreach (EdgeLoop loop in cutter.CutLoops) {
            if (loop.Vertices.Length < 3) { continue; }// Cannot triangulate loops with less than 3 vertices

            var verts = loop.Vertices.Select(i => loop.Mesh.GetVertex(i));
            var polygon = new Polygon2d(verts.Select(v => new Vector2d(v.x, v.y)));

            // Triangulation generally works best with Counter-Clockwise (CCW) polygons.
            // MeshPlaneCut usually provides CCW loops, but it's good practice to ensure it.
            if (polygon.IsClockwise) { polygon.Reverse(); }

            //create polygon
            var poly = new Polygon();
            var contour = new Contour(polygon.Vertices.Select(v => new Vertex(v.x, v.y)));
            poly.Add(contour);

            foreach (var t in new GenericMesher().Triangulate(poly).Triangles) {
                //add verts
                var p0 = t.GetVertex(0);
                var l0 = result.AppendVertex(ToVector3d(p0, z_height));

                var p1 = t.GetVertex(1);
                var l1 = result.AppendVertex(ToVector3d(p1, z_height));

                var p2 = t.GetVertex(2);
                var l2 = result.AppendVertex(ToVector3d(p2, z_height));

                //link those verts to triangles
                result.AppendTriangle(l0, l1, l2);
            }
        }
        return new MeshModel(result);
    }

    private static Vector3d ToVector3d(Vertex v, double z) => new Vector3d(v.X, v.Y, z);
}
