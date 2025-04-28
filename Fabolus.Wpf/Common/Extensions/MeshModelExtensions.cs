using Fabolus.Core.Meshes;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Common.Extensions;

public static class MeshModelExtensions {

    public static MeshGeometry3D ToGeometry(this MeshModel mesh) {
        if (mesh.IsEmpty()) { return new MeshGeometry3D(); }

        var geometry = new MeshBuilder(true, false, false);

        geometry.Append(
            mesh.VectorList().Select(values => values.ToVector3()).ToList(), //3d vert positions
            mesh.TrianglesList().ToList(), //index of each triangle's vertex
            mesh.NormalsList().Select(values => values.ToVector3()).ToList(), //normals
            null); // texture coordinates

        return geometry.ToMeshGeometry3D();       
    }

    private static Vector3 ToVector3(this double[] values) =>
        new((float)values[0], (float)values[1], (float)values[2]);
}
