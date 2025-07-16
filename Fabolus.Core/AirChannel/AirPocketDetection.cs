using Fabolus.Core.Meshes;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel;
public static class AirPockets {

    /// <summary>
    /// Detects which vertices are the highest of their neighbours
    /// </summary>
    /// <param name="model"></param>
    /// <param name="tolerance"></param>
    /// <returns>An array of double[x,y,z] values representing the vertices.</returns>
    public static double[][] Detect(MeshModel model, double tolerance = 0.01) {
        DMesh3 mesh = model.Mesh;

        List<double[]> results = [];


        Vector3d vert, neghbour;
        bool is_not_valid;
        foreach (int id in mesh.VertexIndices()) {
            is_not_valid = false;
            vert = mesh.GetVertex(id);
            var count = mesh.VtxVerticesItr(id).Count();
            foreach (int vId in mesh.VtxVerticesItr(id)) {
                // if not valid
                neghbour = mesh.GetVertex(vId);
                if (neghbour.z < vert.z) { continue; } //good, keep checking

                // bad, break and do not add this vert
                is_not_valid = true;
                break;
            }

            if (is_not_valid) { continue; } // skip adding this one

            results.Add(new double[] { vert.x, vert.y, vert.z });
        }

        return results.ToArray();
    }

}

