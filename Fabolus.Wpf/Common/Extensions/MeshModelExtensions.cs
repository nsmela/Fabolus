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
            mesh.TriangleIndexes().ToList(), //index of each triangle's vertex
            mesh.NormalVectors().Select(v => new Vector3 ((float)v.Item1, (float)v.Item2, (float)v.Item3)).ToArray(), //normals
            null); // texture coordinates

        return geometry.ToMeshGeometry3D();
    }

    public static MeshModel ToMeshModel(this MeshGeometry3D mesh) {
        if (mesh is null || mesh.TriangleIndices.Count == 0) { return new MeshModel(); }
        var vectors = mesh.Positions.Select(v => (v.X, v.Y, v.Z));
        var triangleIndexes = mesh.TriangleIndices.Select(t => (t.Item1, t.Item2, t.Item3));
        return new MeshModel(vectors, triangleIndexes);
    }

}
