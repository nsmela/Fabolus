using Fabolus.Core.Common;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Core;
public static class Extensions {
    public static DMesh3 ToDMesh(this MeshGeometry3D mesh) {
        if (mesh is null || mesh.Positions.Count == 0) { return new DMesh3(); }

        List<Vector3d> vertices = new();

        foreach (var point in mesh.Positions) {
            vertices.Add(new Vector3d(point.X, point.Y, point.Z));
        }

        List<Vector3f>? normals = new();
        foreach (var normal in mesh.Normals) {
            normals.Add(new Vector3f(normal.X, normal.Y, normal.Z));
        }

        if (normals.Count == 0) { normals = null; }

        List<Index3i> triangles = new();
        for (int i = 0; i < mesh.TriangleIndices.Count; i += 3) {
            triangles.Add(new Index3i(mesh.TriangleIndices[i], mesh.TriangleIndices[i + 1], mesh.TriangleIndices[i + 2]));
        }

        //converting the meshes to use Implicit Surface Modeling
        return DMesh3Builder.Build(vertices, triangles, normals);
    }

    public static MeshGeometry3D ToGeometry(this DMesh3 mesh) {
        if (mesh is null || mesh.VertexCount == 0) { return new MeshGeometry3D(); }

        //compacting the DMesh to the indices are true
        MeshGeometry3D geometry = new();

        //calculate positions and normals
        geometry.Positions = new();
        foreach (var vert in mesh.Vertices()) {
            geometry.Positions.Add(new Vector3((float)vert.x, (float)vert.y, (float)vert.z));
        }

        //calculate faces
        geometry.TriangleIndices = new();
        foreach (var i in mesh.Triangles()) {
            geometry.TriangleIndices.Add(i.a);
            geometry.TriangleIndices.Add(i.b);
            geometry.TriangleIndices.Add(i.c);
        }

        geometry.Normals = MeshHelper.CalculateNormals(geometry);

        return geometry;

    }
}
