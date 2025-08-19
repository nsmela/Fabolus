using Fabolus.Core.Extensions;
using g3;
using gs;
using static MR.DotNet;
using static MR.DotNet.MeshComponents;

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

    internal static Result<MeshModel[]> BooleanSplit(Mesh body, Mesh tool, float gap_distance) {
        try {
            BooleanParameters parameters = new() {
                rigidB2A = new AffineXf3f(new MR.DotNet.Vector3f(0, gap_distance, 0))
            };

            MeshModel[] meshes = new MeshModel[2];
            var result = Boolean(body, tool, BooleanOperation.DifferenceAB, parameters);
            meshes[0] = new MeshModel((Mesh)result.mesh);

            result = Boolean(body, tool, BooleanOperation.Intersection, parameters);
            meshes[1] = new MeshModel((Mesh)result.mesh);
            return meshes;
        }
        catch (Exception e) {
            return new MeshError($"Boolean Split failed: {e.Message}");
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
