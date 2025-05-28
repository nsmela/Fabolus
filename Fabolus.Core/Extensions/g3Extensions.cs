using g3;
using static MR.DotNet;

namespace Fabolus.Core.Extensions;

public static class g3Extensions {

    public static DMesh3 BooleanSubtract(this MeshEditor editor, DMesh3 tool)
        => BooleanSubtraction(editor.Mesh, tool);

    public static DMesh3 BooleanSubtraction(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
        return result.mesh.ToDMesh();

    }

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

        gs.MeshAutoRepair repair = new(editor.Mesh);

        return editor;
    }

    public static Mesh ToMesh(this DMesh3 mesh) {
        List<MR.DotNet.Vector3f> verts = mesh.Vertices().Select(v => new MR.DotNet.Vector3f((float)v.x, (float)v.y, (float)v.z)).ToList();
        List<ThreeVertIds> tris = mesh.Triangles().Select(t => new ThreeVertIds(t.a, t.b, t.c)).ToList();
        return Mesh.FromTriangles(verts, tris);
    }

    public static double[] ToArray(this g3.Vector3f vector) => new double[] { vector.x, vector.y, vector.z };
    public static Vector3d ToVector3d(this double[] array) => new Vector3d(array[0], array[1], array[2]);

    public static bool IsEmpty(this DMesh3 mesh) => mesh.VertexCount == 0 || mesh.TriangleCount == 0;

}
