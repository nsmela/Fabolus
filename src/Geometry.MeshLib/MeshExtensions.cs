using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;

namespace GeometryMeshLib;

internal static class MeshExtensions
{
    public static MR.Mesh ToMRMesh(this IMesh mesh)
    {
        var mrMesh = new MR.Mesh();
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        mrMesh.points.vec.resize((ulong)vertices.Length);

        for (int i = 0; i < vertices.Length; i++)
        {
            mrMesh.points.vec[(ulong)i] = new MR.Vector3f(vertices[i].X, vertices[i].Y, vertices[i].Z);
        }

        using var vertTriples = new MR.Std.Vector_MRVertId();
        vertTriples.resize((ulong)triangles.Length);
        for (int i = 0; i < triangles.Length; i++)
        {
            vertTriples[(ulong)i] = new MR.VertId(triangles[i]);
        }

        MR.MeshBuilder.addTriangles(mrMesh.topology, vertTriples, null);
        mrMesh.invalidateCaches();

        return mrMesh;
    }

    public static IMesh ToIMesh(this MR.Mesh mrMesh, MeshMetadata metadata)
    {
        var pVerts = mrMesh.topology.getValidVerts();
        var pPts = mrMesh.points.vec;
        ulong vertCap = mrMesh.points.vec.size(); // The array might have gaps if vertices were deleted

        // We need to map old vertex IDs to new continuous indices
        var vertices = new List<Vector3>((int)vertCap);
        var vertexMap = new int[vertCap]; 

        for (ulong i = 0; i < vertCap; i++)
        {
            var vid = new MR.VertId((int)i);
            if (pVerts.test(vid))
            {
                vertexMap[i] = vertices.Count;
                var pt = pPts[i];
                vertices.Add(new Vector3(pt.x, pt.y, pt.z));
            }
            else
            {
                vertexMap[i] = -1;
            }
        }

        var pFaces = mrMesh.topology.getValidFaces();
        ulong faceCap = mrMesh.topology.faceCapacity();
        var triangles = new List<int>();

        for (ulong i = 0; i < faceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (pFaces.test(fid))
            {
                var tri = mrMesh.topology.getTriVerts(fid);
                int v0 = tri.elems._0.get();
                int v1 = tri.elems._1.get();
                int v2 = tri.elems._2.get();

                triangles.Add(vertexMap[v0]);
                triangles.Add(vertexMap[v1]);
                triangles.Add(vertexMap[v2]);
            }
        }

        return new MRMesh(vertices.ToArray(), triangles.ToArray(), metadata);
    }
}
