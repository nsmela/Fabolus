using Fabolus.Core.Extensions;
using g3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Core.Meshes.PartingTools.PartingTools;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools {

    public static IEnumerable<int> GeneratePartingLine(MeshModel model) {
        List<Vector3d> result = [];

        // we collect the closest points to each corner of the model
        // these are used to determine the parting line
        // by graphing the path between each

        // max x, y, max z
        var bounds = model.Mesh.CachedBounds;
        var tree = new DMeshAABBTree3(model, true);

        Vector3d target;
        int[] id = new int[4];

        target = bounds.Max;
        target.y = 0;
        id[0] = tree.FindNearestVertex(target);

        target.z = bounds.Min.z;
        id[1] = tree.FindNearestVertex(target);

        target.x = bounds.Min.x;
        id[2] = tree.FindNearestVertex(target);

        target.z = bounds.Max.z;
        id[3] = tree.FindNearestVertex(target);

        List<int> path = [];
        Graph graph = new(model);

        path.AddRange(graph.FindPath(id[0], id[1]));
        path.AddRange(graph.FindPath(id[1], id[2]));
        path.AddRange(graph.FindPath(id[2], id[3]));
        path.AddRange(graph.FindPath(id[3], id[0]));

        // remove paths that could skip a triangle
        for (int i = path.Count; i < 1; i--) { // going in reverse to allow removing from list
            int v0 = path[i - 1];
            int v1 = path[i];
            int v2 = path[(i + 1) % path.Count];
            if (SkipThisVertex(model.Mesh, v0, v1, v2)) {
                path.RemoveAt(i);
            }
        }

        GeodiscPathing geodisc = new(model, path);
        geodisc.Compute();
        return geodisc.Path();
    }

    private static bool SkipThisVertex(DMesh3 mesh, int v0, int v1, int v2) {
        var path_distance = mesh.GetVertex(v0).Distance(mesh.GetVertex(v1)) +
                            mesh.GetVertex(v1).Distance(mesh.GetVertex(v2));

        return path_distance > mesh.GetVertex(v0).Distance(mesh.GetVertex(v2)); // skip if the path is longer than the direct distance
    }

    public static List<Vector3[]> PartingPath(MeshModel model) {
        DMesh3 mesh = model.Mesh;

        MeshRegionBoundaryLoops loops = new(mesh, mesh.TriangleIndices().ToArray());

        List<Vector3[]> points = [];
        foreach (g3.EdgeLoop loop in loops) {
            if (loop.EdgeCount < 10) { continue; }

            List<g3.Vector3f> p = [];
            p.AddRange(loop.Vertices.Select(vId => (g3.Vector3f)mesh.GetVertex(vId)));
            points.Add(p.Select(p => new Vector3(p.x, p.y, p.z)).ToArray());
        }

        return points;
    }

    public static List<int[]> PartingPathIndices(MeshModel model) {
        DMesh3 mesh = model.Mesh;

        MeshRegionBoundaryLoops loops = new(mesh, mesh.TriangleIndices().ToArray());

        List<int[]> points = [];
        foreach (g3.EdgeLoop loop in loops) {
            if (loop.EdgeCount < 10) { continue; }

            List<int> p = [];
            p.AddRange(loop.Vertices);
            points.Add(p.ToArray());
        }

        return points;
    }

    public static Vector3[] GeneratePartingLine(MeshModel model, Vector3 pull_direction) {
        DMesh3 mesh = model.Mesh;
        Vector3d dir = pull_direction.ToVector3d();

        HashSet<int> verts = [];
        HashSet<int> visited = [];

        Queue<int> queue = [];
        for(int i = 0; i < mesh.EdgeCount; i++) {
            // check if each triangle is on opposite sides of being perpendicular to pull direction

            if (!IsPartingEdge(mesh, i, dir)) { continue; }

            Index2i edgeV = mesh.GetEdgeV(i);
            verts.Add(edgeV.a);
            visited.Add(edgeV.a);
            queue.Enqueue(edgeV.b);
            break;
        }

        while (queue.Count > 0) {
            int current = queue.Dequeue();
            verts.Add(current);

            foreach (int vId in mesh.VtxVerticesItr(current)) {
                // check for invalid vId
                if (vId == DMesh3.InvalidID) { continue; }
                if (vId == current) { continue; }
                if (visited.Contains(vId)) { continue; }

                int eId = mesh.FindEdge(current, vId);
                if (eId == DMesh3.InvalidID) { continue; }

                if (!IsPartingEdge(mesh, eId, dir)) { continue; }

                // this is a valid vId, process it
                visited.Add(current);
                queue.Enqueue(vId);
                break;
            }
        }

        return verts.Select(i => mesh.GetVertex(i).ToVector3()).ToArray();
    }

    private static bool IsPartingEdge(DMesh3 mesh, int eId, Vector3d dir) {
        // check if each triangle is on opposite sides of being perpendicular to pull direction
        Index2i edgeT = mesh.GetEdgeT(eId);

        bool is_a_pos = mesh.GetTriNormal(edgeT.a).AngleD(dir) < 90.0;
        bool is_b_pos = mesh.GetTriNormal(edgeT.b).AngleD(dir) < 90.0;

        return (is_a_pos != is_b_pos);
    }
}
