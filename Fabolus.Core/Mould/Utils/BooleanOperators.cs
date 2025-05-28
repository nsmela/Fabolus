using g3;
using static MR.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fabolus.Core.Meshes;
using Fabolus.Core.Extensions;
using ManifoldNET;

namespace Fabolus.Core.Mould.Utils;
public static class BooleanOperators {

    public static Result<DMesh3> Subtraction(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        try { 
            var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
            return Result<DMesh3>.Pass(result.mesh.ToDMesh());
        } catch(Exception e) {
            return Result<DMesh3>.Fail([ new MeshError(e.Message) ]);
        }

    }
    
    public static Result<DMesh3> Cut(DMesh3 body, DMesh3 tool) {
        Manifold bodyManifold = body.ToManifold();
        if ( bodyManifold.IsEmpty) {
            return Result<DMesh3>.Fail([new MeshError("Body mesh failed to make manifold: " + bodyManifold.Status.ToString())]);
        }
        MeshGLData data = new();

        Manifold toolManifold = tool.ToManifold();
        if (toolManifold.IsEmpty) {
            return Result<DMesh3>.Fail([new MeshError("Tool mesh failed to make manifold: " + toolManifold.Status.ToString())]);
        }

        Manifold manifold = Manifold.Difference(bodyManifold, toolManifold);
        if (manifold.IsEmpty) {
            return Result<DMesh3>.Fail([new MeshError("Failed to cut: " + manifold.Status.ToString())]);
        }

        return Result<DMesh3>.Pass(manifold.ToDMesh());
    }
}

