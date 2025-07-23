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

}
