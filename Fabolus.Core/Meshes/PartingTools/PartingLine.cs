using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Core.Meshes.PartingTools.PartingTools;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools
{
    /// <summary>
    /// Calculates the parting line of a model
    /// </summary>
    /// <param name="model"></param>
    /// <param name="path_verts"></param>
    /// <param name="pull_direction"></param>
    /// <param name="extrusion_length"></param>
    /// <returns>An ordered array of indexes of the points along the mesh representing the parting line</returns>
    public static Result<Vector3[]> PartingLine(MeshModel model, DraftCollection drafts) {
        // get triangle ids of the negative pull direction region on the mesh
        var region_ids = drafts.GetRegion(DraftClassification.NEGATIVE).ToArray();
        var path = model.GetBorderEdgeLoop(region_ids).ToArray(); // a list of vert IDs on the mesh
        Smooth(model, ref path);

        return model.GetVertices(path).Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
    }

    private static void Smooth(DMesh3 mesh, ref int[] path)
    {
        List<int> smoothPath = [];

        int eId0, eId1;
        Index4i e0, e1;
        int tId;
        double distance;
        for (int i = 1; i < path.Length - 1; i++)
        {
            // reset
            tId = -1;

            // find triangles to the vertices
            eId0 = mesh.FindEdge(path[i - 1], path[i]);
            if (eId0 < 0)
            {
                // no edge found, add the current vertex
                smoothPath.Add(path[i]);
                continue;
            }

            e0 = mesh.GetEdge(eId0);

            eId1 = mesh.FindEdge(path[i], path[i + 1]);
            if (eId1 < 0)
            {
                // no edge found, add the current vertex
                smoothPath.Add(path[i]);
                continue;
            }

            e1 = mesh.GetEdge(eId1);

            // get id of shared triangle
            if (e0.c == e1.c || e0.c == e1.d) { tId = e0.c; }
            if (e0.d == e1.c || e0.d == e1.d) { tId = e0.d; }

            // no triangle found, add the current vertex
            if (tId < 0)
            {
                smoothPath.Add(path[i]);
                continue;
            }

            // new edge would be longer than the current edge, add current vertex
            distance = mesh.DistanceBetweenVectors(path[i - 1], path[i + 1]);
            if (e0.Length < distance)
            {
                smoothPath.Add(path[i]);
                continue;
            }
        }

        path = smoothPath.ToArray();
    }

    private static double DistanceBetweenVectors(this DMesh3 mesh, int vId0, int vId1) =>
        mesh.GetVertex(vId0).Distance(mesh.GetVertex(vId1));
}
