using Fabolus.Core.Meshes;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Common.Extensions;
public static class MeshModelExtensions {
    public static MeshGeometry3D ToGeometry(this MeshModel mesh) {
        if (mesh.IsEmpty()) { return new MeshGeometry3D(); }

        var geometry = new MeshBuilder(true, false, false);

        geometry.Append(
            mesh.TriangleVectors().Select(v => new Point3D(v.Item1, v.Item2, v.Item3)).ToArray(), //3D vert positions
            mesh.TriangleIndices(), //index of each triangle's vertex
            mesh.NormalVectors().Select(v => new Vector3 ((float)v.Item1, (float)v.Item2, (float)v.Item3)).ToArray(), //normals
            null); // texture coordinates

        return geometry.ToMeshGeometry3D();
    }

    private static List<int> TrianglesList((int, int, int) triangles) {
        var triangles = new List<int>();
        foreach (var tri in triangles) {
            triangles.Add(tri);
            triangles.Add(tri.b);
            triangles.Add(tri.c);
        }
        return triangles;
    }
}
