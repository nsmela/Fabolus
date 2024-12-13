using Fabolus.Wpf.Common.Mesh;
using g3;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Common.Extensions;
public static class DMeshExtensions {
    public static MeshGeometry3D ToGeometry(this DMesh3 mesh) {
        if (mesh is null || mesh.VertexCount == 0) { return new MeshGeometry3D(); }
        MeshNormals.QuickCompute(mesh);

        var geometry = new MeshBuilder(true, false, false);

        geometry.Append(
            VectorList(mesh), //3d vert positions
            TrianglesList(mesh), //index of each triangle's vertex
            NormalsList(mesh), //normals
            null); // texture coordinates

        return geometry.ToMeshGeometry3D();
    }

    private static List<Vector3> VectorList(DMesh3 mesh) =>
        mesh.Vertices().Select(v => v.ToVector3()).ToList();

    private static List<int> TrianglesList(DMesh3 mesh) {
        var triangles = new List<int>();
        foreach(var tri in mesh.Triangles()) {
            triangles.Add(tri.a); 
            triangles.Add(tri.b); 
            triangles.Add(tri.c);
        }
        return triangles;
    }

    private static List<Vector3> NormalsList(DMesh3 mesh) {
        var normals = new List<Vector3>();
        for (int i = 0; i < mesh.VertexCount; i++) {
            normals.Add(mesh.GetVertexNormal(i).ToVector3());
        }
        return normals;
    }

}
