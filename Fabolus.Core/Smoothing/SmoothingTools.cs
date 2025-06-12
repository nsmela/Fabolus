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

    //get texture coordinates
    public static float[] GenerateTextureCoordinates(Bolus newBolus, Bolus oldBolus) {
        var lower = -2.0f;
        var upper = 2.0f;
        var spread = upper - lower;
        var mesh = oldBolus.Mesh;
        var spatial = new DMeshAABBTree3(mesh); //TODO: move to store the spatial on Bolus to improve speed
        spatial.Build();

        var values = new List<float>();
        foreach(var v in newBolus.Mesh.Vectors()) { //TODO: convert to parallel, but account for safesetting and race conditions
            var point = v.ToVector3d();
            var distance = DistanceToMesh(point, mesh, spatial);
            values.Add(DistanceToRatio(lower, upper, spread, spatial.IsInside(point), distance));
        }
        return values.ToArray();
    }

    private static float DistanceToMesh(Vector3d point, DMesh3 mesh, DMeshAABBTree3 spatial) =>
        (float)MeshQueries.NearestPointDistance(mesh, spatial, point, 10.0);

    private static float DistanceToRatio(float lower, float upper, float spread, bool isInside, float distance) {
        var value = isInside
            ? Math.Max(distance * -1, lower) //make negative if within the mesh and test against lowest value
            : Math.Min(distance, upper); //test against highest value
        value -= lower; //converting from distance to value on the spread by subtracting lower to make lowest possible value equal 0
        
        return value / spread; //convert to a ratio
    }

    //find smooth surfaces
    public static MeshModel[] GetSmoothSurfaces(MeshModel model, double rads_threshold = Math.PI / 27.0f) {
        DMesh3 mesh = model.Mesh;

        List<int> stack = mesh.TriangleIndices().ToList();
        List<MeshModel> results = new();
        while (stack.Count() > 0) {
            results.Add(GetSmoothTriangles(mesh, ref stack, rads_threshold));
        }

        return results.ToArray();
    }

    private static MeshModel GetSmoothTriangles(DMesh3 mesh, ref List<int> stack, double rads_threshold) {
        Queue<int> queue = new();
        queue.Enqueue(stack[0]); //start with the first triangle

        List<int> good = new() { stack[0] };
        stack.RemoveAt(0); //remove the first triangle from the stack

        while (queue.Count > 0) {
            int tri_id = queue.Dequeue();
            Index3i current = mesh.GetTriangle(tri_id);
            Vector3d normal = mesh.GetTriNormal(tri_id);
            Index3i neighbours = mesh.GetTriNeighbourTris(tri_id);
            foreach (var id in neighbours.array) {
                if (id < 0 || !stack.Contains(id)) { continue; } //skip if no neighbour or already processed
                var neighbourNormal = mesh.GetTriNormal(id);
                var angle = Vector3d.AngleR(normal, neighbourNormal);
                if (angle < rads_threshold) {
                    queue.Enqueue(id);
                    good.Add(id);
                    stack.Remove(id); //remove from stack to avoid reprocessing
                }
            }
        }

        //build mesh with good ids
        if (good.Count > 0) {
            DMesh3 result = new();
            foreach (var id in good) {
                Index3i triangle = mesh.GetTriangle(id);
                var a = result.AppendVertex(mesh.GetVertex(triangle.a));
                var b = result.AppendVertex(mesh.GetVertex(triangle.b));
                var c = result.AppendVertex(mesh.GetVertex(triangle.c));

                result.AppendTriangle(a, b, c);
            }
            return new MeshModel(result);
        }
        return new MeshModel(); //return empty mesh if no good triangles found
    }

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
