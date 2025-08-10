using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static MeshModel GetHoles(MeshModel model) =>
        new MeshModel(GetHoles(model.Mesh));

    /// <summary>
    /// Returns a mesh showing the missing triangles
    /// </summary>
    /// <param name="mesh"></param>
    /// <returns></returns>
    internal static DMesh3 GetHoles(DMesh3 mesh) {
        MeshBoundaryLoops loops = new(mesh);
        foreach (var loop in loops) {
            if (loop.EdgeCount != 3) { continue; }

            HashSet<int> vIds = [];
            foreach (var v in loop.Vertices) {
                if (vIds.Contains(v)) { continue;}

                vIds.Add(v);
            }

            var number = vIds.Count;
        }
        return default;
    }
}
