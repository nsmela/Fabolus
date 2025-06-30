using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System;
using System.Windows;
using static MR.DotNet;

namespace Fabolus.Core.Smoothing;

public record struct WeightedOffsetSettings(float InflateDistance, float WeightValue, float SmoothingAngleDegs, double CellSize);

public class WeightedOffsetSmoothing {

    public static MeshModel Smooth(Bolus bolus, WeightedOffsetSettings settings) {
        float inflateDistance = settings.InflateDistance;
        float smoothAngleRads = (float)Math.PI * settings.SmoothingAngleDegs / 180.0f;
        float cell_size = (float)settings.CellSize;
        float weightedValue = settings.WeightValue;

        // find largest two smoothed surfaces as these represent the area we don't want to affect
        var surfaces = GetSmoothSurfaces(bolus.Mesh, smoothAngleRads);
        Array.Sort(surfaces, (a, b) => b.TriangleCount.CompareTo(a.TriangleCount));

        // populate the weighted array
        int[] indexes = surfaces[0].TriangleIndices().Concat(surfaces[1].TriangleIndices()).ToArray();
        float[] weights = new float[bolus.Mesh.Mesh.TriangleCount];// Enumerable.Repeat<float>(0.0f, bolus.Mesh.Mesh.TriangleCount).ToArray();
        int index = -1;
        for (int i = 0; i < indexes.Length; i ++) {
            index = indexes[i];
            weights[index] = weightedValue;
        }

        // apply weighted offset
        return MeshTools.WeightedOffsetMesh(bolus.Mesh, weights, inflateDistance, cell_size);
    }

    // This method groups the triangles of the mesh into smooth surfaces based on the angle between their normals.
    private static DMesh3[] GetSmoothSurfaces(DMesh3 mesh, double rads_threshold = Math.PI / 5.0f) {
        List<int> stack = mesh.TriangleIndices().ToList();
        Queue<int> queue = new();
        queue.Enqueue(stack[0]); //start with the first triangle

        List<int> good = new() { stack[0] };
        stack.RemoveAt(0); //remove the first triangle from the stack

        List<DMesh3> results = new();
        while (stack.Count() > 0) {
            results.Add(GetSmoothTriangles(mesh, ref stack, rads_threshold));
        }

        return results.ToArray();
    }

    private static DMesh3 GetSmoothTriangles(DMesh3 mesh, ref List<int> stack, double rads_threshold) {
        Queue<int> queue = new();
        queue.Enqueue(stack[0]); //start with the first triangle

        List<int> good = new() { stack[0] };
        stack.RemoveAt(0); //remove the first triangle from the stack
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
        return new(); //return empty mesh if no good triangles foundAdd commentMore actions
    }
}

