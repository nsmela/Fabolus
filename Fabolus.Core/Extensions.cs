using Fabolus.Core.Common;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using TriangleNet;
using TriangleNet.Geometry;
using TriangleNet.Meshing;

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

    public static DMesh3 ToDmesh(this TriangleNet.Geometry.Polygon polygon, double z = 0.0) {
        var mesh = new GenericMesher().Triangulate(polygon);

        DMesh3 result = new();
        
        foreach(var point in mesh.Vertices) {
            result.AppendVertex(new Vector3d(point.X, point.Y, z));
        }

        foreach(var tri in mesh.Triangles) {
            var index = new Index3i {
                a = tri.GetVertex(0).ID,
                b = tri.GetVertex(1).ID,
                c = tri.GetVertex(2).ID,
            };

            result.AppendTriangle(index);
        }

        return result;
    }
}
