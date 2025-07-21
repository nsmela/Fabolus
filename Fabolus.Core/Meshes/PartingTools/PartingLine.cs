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
    private const double DEFAULT_SHARP_ANGLE = MathUtil.Deg2Rad * 30.0;

    /// <summary>
    /// Calculates the parting line of a model
    /// </summary>
    /// <returns>An ordered array of indexes of the points along the mesh representing the parting line</returns>
    public static Result<Vector3[]> PartingLine(MeshModel model, DraftCollection drafts) {
        // get triangle ids of the negative pull direction region on the mesh
        var region_ids = drafts.GetDraftRegion(DraftClassification.NEGATIVE).ToArray();
        var path = model.GetBorderEdgeLoop(region_ids).ToArray(); // a list of vert IDs on the mesh
        Smooth(model, ref path);

        return model.GetVertices(path).Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
    }

    private static void Smooth(DMesh3 mesh, ref int[] path) {
        Graph graph = new(mesh);

        List<int> smoothPath = [];

        int eId0, eId1, eId2;
        int v0, v1, v2;
        Index4i e0, e1, e2;
        int tId;
        double distance;
        double length;
        for (int i = 1; i < path.Length - 1; i++) {
            // reset
            tId = -1;

            // simplifying vertex id referencing
            v0 = path[i - 1];
            v1 = path[i];
            v2 = path[i + 1];

            // find the previous edge in the path
            // abort if cannot find it
            // TODO: why are we referencing points that have no edges between?
            // TOD: do this sweep at first to check topology and error, rest of the code would be easier
            if (!GetEdge(mesh, v1, v0, out e0)) {
                smoothPath.Add(v1);
                continue;
            }

            // find the next edge in the path
            // abort if cannot find it
            if (!GetEdge(mesh, v1, v2, out e1)) {
                smoothPath.Add(v1);
                continue;
            }

            // if the edges share a triangle, we can check if the third edge is shorter than the current edges
            // if so, we can skip saving this point to shorten the parting line
            if (ThirdTriangleEdgeShorter(mesh, v0, v1, v2)) {
                continue;
            }

            // if the angle is sharp, we can check if the other path is shorter
            if (SharpAngle(mesh, v0, v1, v2, DEFAULT_SHARP_ANGLE) && OtherPathShorter(mesh, graph, v0, v1, v2, out int[] ids)) {
                // if so, add the new path to the smooth path
                smoothPath.AddRange(ids);
                continue;
            } 

            // if no changes needed, add the current point
            smoothPath.Add(path[i]);
        }

        path = smoothPath.ToArray();
    }

    /// <summary>
    /// If v0, v1, v2 are connected by two triangles, check if the opposing edge vert results in a shorter path
    /// </summary>
    /// <param name="ids">array of points for other path</param>
    /// <returns>If the other point results in a shorter path</returns>
    private static bool OtherPathShorter(DMesh3 mesh, Graph graph, int vId0, int vId1, int vId2, out int[] ids) {
        ids = graph.FindPath(vId0, vId2, true);

        if (ids.Length < 2) { return false; } // no path found

        double original_length = FindLength(mesh, vId0, vId1, vId2); // current edges length
        double graph_length = FindLength(mesh, [vId0, .. ids, vId2]); // new edges length
        return (original_length > graph_length); // if the new path is shorter, return true
    }

    private static bool ThirdTriangleEdgeShorter(DMesh3 mesh, int v0, int v1, int v2) {
        // do edges share a triangle?
        var tId = mesh.FindTriangle(v0, v1, v2);
        if ( tId == DMesh3.InvalidID) { return false; } // do not share a triangle

       
        double distance = DistanceBetweenVectors(mesh, v0, v2); // third edge length
        double length = FindLength(mesh, v0, v1, v2); // current edges length

        return distance < length;
    }

    /// <summary>
    /// Returns true if the angle between v0, v1, v2 is greater than the specified angle in radians.
    /// </summary>
    private static bool SharpAngle(DMesh3 mesh, int vId0, int vId1, int vId2, double angle_rads) {
        Vector3d v0 = mesh.GetVertex(vId0);
        Vector3d v1 = mesh.GetVertex(vId1);
        Vector3d v2 = mesh.GetVertex(vId2);

        Vector3d edge1 = (v0 - v1).Normalized;
        Vector3d edge2 = (v2 - v1).Normalized;

        return edge1.AngleR(edge2) > angle_rads;
    }

    private static bool GetEdge(DMesh3 mesh, int a, int b, out Index4i edge) {
        int id = mesh.FindEdge(a, b);
        if (id < 0) {
            edge = default;
            return false; // invalid ID
        }
        
        edge = mesh.GetEdge(id);
        return true; 
    }

    private static double FindLength(DMesh3 mesh, params int[] ids) {
        if (ids.Length < 2) { return 0.0; } // insufficient ids

        double length = 0.0;
        int current_id = ids[0];
        int next_id;
        for (int i = 1; i < ids.Length; i++) {
            next_id = ids[i];
            length += DistanceBetweenVectors(mesh, current_id, next_id);
            current_id = next_id;
        }

        return length;
    }

    private static double DistanceBetweenVectors(DMesh3 mesh, int vId0, int vId1) =>
        mesh.GetVertex(vId0).Distance(mesh.GetVertex(vId1));
}
