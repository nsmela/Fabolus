using Fabolus.Core.Extensions;
using g3;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    //public static double[] SignedDistances(MeshModel origin, MeshModel target) {
    //    DMeshAABBTree3 tree = new(target.Mesh, true);
    //
    //    List<double> result = [];
    //    foreach (Vector3d v in origin.Mesh.Vertices()) {
    //        int index = tree.FindNearestVertex(v);
    //        double distance = target.Mesh.GetVertex(index).Distance(v);
    //
    //        if (tree.IsInside(v)) { distance *= -1; }// negative distance is inside the mesh
    //
    //        result.Add(distance);
    //    }
    //    
    //    return result.ToArray();
    //}

    public static float[] SignedDistances(MeshModel origin, MeshModel target) {
        Mesh originMesh = origin.Mesh.ToMesh();
        Mesh targetMesh = target.Mesh.ToMesh();
    
        MeshProjectionParameters parameters = new() {
            loDistLimitSq = 0.0f,
            upDistLimitSq = float.MaxValue,
        };
    
        List<float> result = FindSignedDistances(originMesh, targetMesh, parameters);
        return result.ToArray();
    
    }
}
