using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Smoothing;
public static class LaplacianSmoothing {
    //ref: https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh_ops/LaplacianMeshSmoother.cs
    // https://www.cse.wustl.edu/~taoju/cse554/lectures/lect08_Deformation.pdf

    public static Bolus SmoothBolus(Bolus bolus) {
        var mesh = (DMesh3)bolus.Mesh;
        //compute Laplacian coordinates
        var smoother = new LaplacianMeshSmoother(mesh);


        //measure local curvature
        //define an inflation weight
        //modify Laplacian target
        //solve deformation system
        smoother.Initialize();

        foreach(var i in Enumerable.Range(0, mesh.VertexCount)) {
            smoother.SetConstraint(i, mesh.GetVertex(i), 2.0, true);
        }

        smoother.SolveAndUpdateMesh();

        return new Bolus(smoother.Mesh);
    }

    public static MeshModel SmoothSurfaces(Bolus bolus, double angle = Math.PI / 36) {
        Queue<int> tri_queue = new();

        var mesh = (DMesh3)bolus.Mesh;

        DMesh3 result = new();
        List<int> remaining_indexes = mesh.TriangleIndices().ToList();
        //getting the first one
        foreach (var i in mesh.TriangleIndices()) {
            var normal = mesh.GetTriNormal(i);

            var n = mesh.GetTriNeighbourTris(i);
            Vector3d neightbour_normals = new Vector3d();
            if (n.a >= 0) { neightbour_normals.Add(mesh.GetTriNormal(n.a)); }
            if (n.b >= 0) { neightbour_normals.Add(mesh.GetTriNormal(n.b)); }
            if (n.c >= 0) { neightbour_normals.Add(mesh.GetTriNormal(n.c)); }
            neightbour_normals.Normalize();

            //move to next if invalid
            if (normal.AngleR(neightbour_normals) > angle) { continue; }

            tri_queue.Enqueue(i);
            remaining_indexes.Remove(i);
            break;
        }

        while (tri_queue.Count > 0) {
            var i = tri_queue.Dequeue();

            var normal = mesh.GetTriNormal(i);

            var n = mesh.GetTriNeighbourTris(i);
            Vector3d neightbour_normals = new Vector3d();
            if (n.a >= 0) { neightbour_normals.Add(mesh.GetTriNormal(n.a)); }
            if (n.b >= 0) { neightbour_normals.Add(mesh.GetTriNormal(n.b)); }
            if (n.c >= 0) { neightbour_normals.Add(mesh.GetTriNormal(n.c)); }
            neightbour_normals.Normalize();

            //move to next if invalid
            if (normal.AngleR(neightbour_normals) > angle) { continue; }

            // add the triangle
            Index3i tri = mesh.GetTriangle(i);
            int a = result.AppendVertex(mesh.GetVertex(tri.a));
            int b = result.AppendVertex(mesh.GetVertex(tri.b));
            int c = result.AppendVertex(mesh.GetVertex(tri.c));
            result.AppendTriangle(a, b, c);

            //queue up the adjacent triangles
            if (n.a >= 0 && remaining_indexes.Contains(n.a)) {
                tri_queue.Enqueue(n.a);
                remaining_indexes.Remove(n.a);
            }
            if (n.b >= 0 && remaining_indexes.Contains(n.b)) {
                tri_queue.Enqueue(n.b);
                remaining_indexes.Remove(n.b);
            }
            if (n.c >= 0 && remaining_indexes.Contains(n.c)) {
                tri_queue.Enqueue(n.c);
                remaining_indexes.Remove(n.c);
            }
        }

        return new MeshModel(result);
    }
}
