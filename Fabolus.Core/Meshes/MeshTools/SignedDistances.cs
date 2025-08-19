using Fabolus.Core.Extensions;
using g3;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

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
