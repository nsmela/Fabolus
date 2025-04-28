using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace Fabolus.Core.Extensions;

public static class MeshGeometryExtensions {
    public static DMesh3 ToDMesh(this MeshGeometry3D mesh) {
        if (mesh is null || mesh.Positions.Count == 0) { return new DMesh3(); }

        List<Vector3d> vertices = new();

        foreach (var point in mesh.Positions) {
            vertices.Add(new Vector3d(point.X, point.Y, point.Z));
        }

        List<g3.Vector3f>? normals = new();
        foreach (var normal in mesh.Normals) {
            normals.Add(new g3.Vector3f(normal.X, normal.Y, normal.Z));
        }

        if (normals.Count == 0) { normals = null; }

        List<Index3i> triangles = new();
        for (int i = 0; i < mesh.TriangleIndices.Count; i += 3) {
            triangles.Add(new Index3i(mesh.TriangleIndices[i], mesh.TriangleIndices[i + 1], mesh.TriangleIndices[i + 2]));
        }

        //converting the meshes to use Implicit Surface Modeling
        return DMesh3Builder.Build(vertices, triangles, normals);
    }

}
