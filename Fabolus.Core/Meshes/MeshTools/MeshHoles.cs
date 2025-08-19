using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.PartingTools;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static Result<Contour[]> GetHoles(MeshModel model) =>
        GetHoles(model.Mesh);

    /// <summary>
    /// Returns a mesh showing the missing triangles
    /// </summary>
    /// <param name="mesh"></param>
    /// <returns></returns>
    internal static Result<Contour[]> GetHoles(DMesh3 mesh) {
        MeshBoundaryLoops loops = new(mesh);
        List<Contour> contours = [];
        foreach (var loop in loops) {
            if (loop.EdgeCount <= 3) { continue; } // invalid loop

            List<int> vIds = [];
            foreach (var v in loop.Vertices) {
                if (vIds.Contains(v)) { continue;}

                vIds.Add(v);
            }

            System.Numerics.Vector3[] points = vIds.Select(i => mesh.GetVertex(i).ToVector3()).ToArray();
            contours.Add(new Contour { IsClosed = true, Points = points });
        }

        return contours.ToArray();
    }
}
