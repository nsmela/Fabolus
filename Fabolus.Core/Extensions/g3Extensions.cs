using g3;
using System.Numerics;
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

    // System.Numerics Converters 
    // these are the values used to as a sommon value between Fabolus.Core and Fabolus.Wpf
    // to seperate Wpf from using g3 or MeshLib, only standard libraries
    public static Vector3 ToVector3(this Vector3d v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
    public static Vector3 ToVector3(this g3.Vector3f v) => new Vector3(v.x, v.y, v.z);

    public static Vector3d ToVector3d(this Vector3 vector) => new Vector3d(vector.X, vector.Y, vector.Z);

}
