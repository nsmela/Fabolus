using Fabolus.Core.Meshes;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Common.Extensions;

public static class MeshModelExtensions {
    public static MeshGeometry3D ToGeometry(this MeshModel mesh) {
        if (mesh.IsEmpty()) { return new MeshGeometry3D(); }

        var geometry = new MeshBuilder(true, false, false);

        geometry.Append(
            mesh.TriangleVectors().Select(v => new Vector3((float)v.Item1, (float)v.Item2, (float)v.Item3)).ToArray(), //3D vert positions
            mesh.TriangleIndexes().ToList(), //index of each triangle's vertex
            mesh.NormalVectors().Select(v => new Vector3 ((float)v.Item1, (float)v.Item2, (float)v.Item3)).ToArray(), //normals
            null); // texture coordinates

        return geometry.ToMeshGeometry3D();
    }

}
