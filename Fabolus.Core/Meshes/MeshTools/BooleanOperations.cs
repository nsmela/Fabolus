using Fabolus.Core.Extensions;
using g3;
using gs;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static Result<MeshModel> BooleanSubtraction(MeshModel body, MeshModel tool) {
        Mesh bodyMesh = body;
        Mesh toolMesh = tool;

        try {
            var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
            return Result<MeshModel>.Pass(new(result.mesh));
        } catch (Exception e) {
            return Result<MeshModel>.Fail([new MeshError($"Boolean Subtraction failed: {e.Message}")]);
        }
    }
}
