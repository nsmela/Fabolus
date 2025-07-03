using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static int[] SihlouetteCurve(MeshModel model, double[] pullDirection) {
        DMesh3 mesh = new DMesh3(model.Mesh);
        Vector3d direction = new Vector3d(pullDirection[0], pullDirection[1], pullDirection[2]).Normalized;

        // Get the silhouette edges
        List<int> edges = [];

        foreach (int edge in mesh.EdgeIndices()) {
            Index2i triangles = mesh.GetEdgeT(edge);
            if (triangles.a == DMesh3.InvalidID || triangles.b == DMesh3.InvalidID) {
                continue; // Skip boundary edges
            }

            double dotA = mesh.GetTriNormal(triangles.a).Dot(direction);
            double dotB = mesh.GetTriNormal(triangles.b).Dot(direction);

            if (dotA * dotB < 0) {  // The normals of the triangles are facing opposite directions of the perpendicular direction
                edges.Add(edge);
            }

        }

        return edges.Select(e => mesh.GetEdgeV(e).a).ToArray();

    }
}