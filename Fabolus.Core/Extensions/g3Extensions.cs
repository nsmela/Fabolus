using g3;
using static MR.DotNet;

namespace Fabolus.Core.Extensions;

public static class g3Extensions {

    public static Mesh ToMesh(this DMesh3 mesh) {
        List<MR.DotNet.Vector3f> verts = mesh.Vertices().Select(v => new MR.DotNet.Vector3f((float)v.x, (float)v.y, (float)v.z)).ToList();
        List<ThreeVertIds> tris = mesh.Triangles().Select(t => new ThreeVertIds(t.a, t.b, t.c)).ToList();
        return Mesh.FromTriangles(verts, tris);
    }

    public static double[] ToArray(this g3.Vector3f vector) => new double[] { vector.x, vector.y, vector.z };
    public static Vector3d ToVector3d(this double[] array) => new Vector3d(array[0], array[1], array[2]);

    public static bool IsEmpty(this DMesh3 mesh) => mesh.VertexCount == 0 || mesh.TriangleCount == 0;
    public static bool IsEmpty(this Polygon2d polygon) => polygon.Vertices.Count == 0;

}
