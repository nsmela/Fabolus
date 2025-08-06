using Fabolus.Core.Extensions;
using g3;
using gs;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static Result<MeshModel> BooleanSubtraction(MeshModel body, MeshModel tool) {
        Mesh bodyMesh = body;
        Mesh toolMesh = tool;

        return BooleanSubtraction(bodyMesh, toolMesh);
    }

    internal static Result<MeshModel> BooleanSubtraction(Mesh body, Mesh tool) {
        try {
            var result = Boolean(body, tool, BooleanOperation.DifferenceAB);
            return Result<MeshModel>.Pass(new(result.mesh));
        } catch (Exception e) {
            return Result<MeshModel>.Fail([new MeshError($"Boolean Subtraction failed: {e.Message}")]);
        }
    }

    public static Result<MeshModel> BooleanUnion(MeshModel body, MeshModel tool) {
        Mesh bodyMesh = body;
        Mesh toolMesh = tool;

        return BooleanUnion(bodyMesh, toolMesh);
    }

    internal static Result<MeshModel> BooleanUnion(Mesh body, Mesh tool) {
        try {
            var result = Boolean(body, tool, BooleanOperation.Union);
            return Result<MeshModel>.Pass(new(result.mesh));
        }
        catch (Exception e) {
            return Result<MeshModel>.Fail([new MeshError($"Boolean Union failed: {e.Message}")]);
        }
    }
}
