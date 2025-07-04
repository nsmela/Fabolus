using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {
    // This method groups the triangles of the mesh into smooth surfaces based on the angle between their normals.
    public static DMesh3[] GetSmoothSurfaces(DMesh3 mesh, double rads_threshold = Math.PI / 5.0f) {
        List<int> stack = mesh.TriangleIndices().ToList();
        Queue<int> queue = new();
        queue.Enqueue(stack[0]); //start with the first triangle

        List<int> good = new() { stack[0] };
        stack.RemoveAt(0); //remove the first triangle from the stack

        List<DMesh3> results = new();
        while (stack.Count() > 0) {
            results.Add(GetSmoothTriangles(mesh, ref stack, rads_threshold));
        }

        var meshes = results.ToArray();
        Array.Sort(meshes, (a, b) => b.TriangleCount.CompareTo(a.TriangleCount));

        return meshes;
    }

    private static DMesh3 GetSmoothTriangles(DMesh3 mesh, ref List<int> stack, double rads_threshold) {
        List<int> good = new() { stack[0] };
        Queue<int> queue = new();
        queue.Enqueue(stack[0]); //start with the first triangle

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
            return result;
        }
        return new(); //return empty mesh if no good triangles found
    }
}
