using g3;
using static MR.DotNet;

namespace Fabolus.Core.Extensions;

public static class MRMeshExtensions {

    //ref: https://github.com/MeshInspector/MeshLib/tree/master/source/MRDotNet

    public static DMesh3 ToDMesh(this Mesh mesh) {
        DMesh3 result = new();

        foreach (var p in mesh.Points) {
            result.AppendVertex(new Vector3d(p.X, p.Y, p.Z));
        }
        foreach (var t in mesh.Triangulation) {
            result.AppendTriangle(t.v0.Id, t.v1.Id, t.v2.Id);
        }

        return result;
    }

    public static System.Numerics.Vector3 ToVector3(this MR.DotNet.Vector3f v) => new System.Numerics.Vector3(v.X, v.Y, v.Z);
    public static IEnumerable<System.Numerics.Vector3> ToVector3(this IEnumerable<MR.DotNet.Vector3f> vectors) => vectors.Select(v => v.ToVector3());
}
