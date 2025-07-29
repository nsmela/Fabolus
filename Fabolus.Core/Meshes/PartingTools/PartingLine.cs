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

        return path;
    }

    /// <summary>
    /// Determines if a segment is classified as twisted
    /// </summary>
    /// <param name="p0">Previous segment's origin</param>
    /// <param name="p1">Current segment's origin</param>
    /// <param name="p2">Current segment's end</param>
    /// <param name="threshold">value from 1.0 (aligned) to 0.0 (perpendicular) to -1.0 (reversed) </param>
    /// <returns>True/False</returns>
    private static bool IsTwisted(Vector3d p0, Vector3d p1, Vector3d p2, double threshold = 0.0) {
        // check if the path is twisted by checking the cross product of the vectors
        Vector3d v0 = (p1 - p0).Normalized;
        Vector3d v1 = (p2 - p1).Normalized;

        double dot = v0.Dot(v1);
        return dot < threshold;
    }

    public static Result<Vector3[]> OrientedPartingLine(MeshModel model) {
        var path = GeneratePartingLine(model);
        var value = path.Select(i => model.Mesh.GetVertex(i)).ToArray();

        return path.ToArray().Select(i => model.Mesh.GetVertex(i).ToVector3()).ToArray();


        GeodiscPathing geodisc = new(model, path);
        geodisc.Compute();

        return geodisc.Path().Select(i => model.Mesh.GetVertex(i).ToVector3()).ToArray();
    }

    public static Result<Vector3[]> OrientedPartingLine(MeshModel model, IEnumerable<int> path_ids) => 
        path_ids.ToArray().Select(i => model.Mesh.GetVertex(i).ToVector3()).ToArray();


    private static bool SkipThisVertex(DMesh3 mesh, int v0, int v1, int v2) {
        var path_distance = mesh.GetVertex(v0).Distance(mesh.GetVertex(v1)) +
                            mesh.GetVertex(v1).Distance(mesh.GetVertex(v2));

        return path_distance > mesh.GetVertex(v0).Distance(mesh.GetVertex(v2)); // skip if the path is longer than the direct distance
    }

}
