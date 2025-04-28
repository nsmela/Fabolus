using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Extensions;

public static class g3Extensions {

    public static DMesh3 BooleanSubtract(this MeshEditor editor, DMesh3 tool)
        => Subtraction(editor.Mesh, tool);

    /// <summary>
    /// Adds multiple meshes to the main main.
    /// Useful if the meshes do not overlap.
    /// </summary>
    /// <param name="editor"></param>
    /// <param name="tools"></param>
    /// <returns></returns>
    public static MeshEditor Join(this MeshEditor editor, IList<DMesh3> tools) {
        foreach (var mesh in tools) {
            editor.AppendMesh(mesh);
        }

        return editor;
    }

    public static DMesh3 Subtraction(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
        return result.mesh.ToDMesh();

    }

    public static Mesh ToMesh(this DMesh3 mesh) {
        List<MR.DotNet.Vector3f> verts = mesh.Vertices().Select(v => new MR.DotNet.Vector3f((float)v.x, (float)v.y, (float)v.z)).ToList();
        List<ThreeVertIds> tris = mesh.Triangles().Select(t => new ThreeVertIds(t.a, t.b, t.c)).ToList();
        return Mesh.FromTriangles(verts, tris);
    }

    public static double[] ToArray(this g3.Vector3f vector) => new double[] { vector.x, vector.y, vector.z };
}
