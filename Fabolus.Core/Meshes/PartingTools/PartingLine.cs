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

    /// <summary>
    /// Calculates the parting line of a model
    /// </summary>
    /// <returns>An ordered array of indexes of the points along the mesh representing the parting line</returns>
    public static Result<Vector3[]> PartingLine(MeshModel model, DraftCollection drafts) {
        // get triangle ids of the negative pull direction region on the mesh
        var region_ids = drafts.GetDraftRegion(DraftClassification.NEGATIVE).ToArray();

        var path = model.GetBorderEdgeLoop(region_ids).ToArray(); // a list of vert IDs on the mesh
        Smooth(model, ref path);

        // ensure the contour points are evenly spaced
        var result = EvenEdgeLoop.Generate(path.Select(vId => model.Mesh.GetVertex(vId)), 100); //.Generate(path.Select(vId => model.Mesh.GetVertex(vId)), 100);
        //return path.Select(vId => model.Mesh.GetVertexf(vId))
            //.Select(v => new Vector3(v.x, v.y, v.z))
            //.ToArray();
        return result.Select(v => new Vector3((float)v.x, (float)v.y, (float)v.z)).ToArray();
    }

    private static void Smooth(DMesh3 mesh, ref int[] path) {

        // perform a geodisc pathing check
        GeodiscPathing geodisc = new(mesh, path);
        geodisc.Compute();

        path = geodisc.Path().ToArray();
    }

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

    // testing for pathing cleanup
    /// <summary>
    /// Checks for defective segments
    /// </summary>
    /// <param name="model"></param>
    /// <param name="path_ids"></param>
    /// <returns>A list of Vector3 [origin, end] for the defective segments</returns>
    public static List<Vector3[]> SegmentIntersections(IEnumerable<Vector3> path) {
        Vector3[] points = path.ToArray();

        List<Vector3[]> result = new();
        for(int i = 1; i < points.Count(); i++) {
            Vector3 v0 = points[(i - 1) % points.Count()]; // previous segment's origin
            Vector3 v1 = points[i];
            Vector3 v2 = points[(i + 1) % points.Count()]; // current segment's end
            // check if the segment is twisted
            if (IsTwisted(v0.ToVector3d(), v1.ToVector3d(), v2.ToVector3d())) {
                result.Add([v1, v2]); // add the middle point of the twisted segment
            }
        }

        return result;
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
