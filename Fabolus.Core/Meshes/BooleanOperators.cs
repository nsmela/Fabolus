using Fabolus.Core.Extensions;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Meshes;

internal static class BooleanOperators {
    public static Result<DMesh3> Subtraction(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        try {
            var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
            return Result<DMesh3>.Pass(result.mesh.ToDMesh());
        } catch (Exception e) {
            return Result<DMesh3>.Fail([new MeshError($"Boolean Subtraction failed: {e.Message}")]);
        }

    }

    public static Result<DMesh3> Intersect(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        try {
            var result = Boolean(bodyMesh, toolMesh, BooleanOperation.Intersection);
            return Result<DMesh3>.Pass(result.mesh.ToDMesh());
        } catch (Exception e) {
            return Result<DMesh3>.Fail([new MeshError($"Boolean Intersection failed: {e.Message}")]);
        }

    }
}
