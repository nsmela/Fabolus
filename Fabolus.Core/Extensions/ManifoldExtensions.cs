using g3;
using ManifoldNET;

namespace Fabolus.Core.Extensions;

public static class ManifoldExtensions {

    public static Manifold ToManifold(this DMesh3 mesh) {
        List<float> verts = new();
        foreach (var v in mesh.Vertices().Select(v => (Vector3f)v)) {
            verts.Add(v.x);
            verts.Add(v.y);
            verts.Add(v.z);
        }

        List<uint> indexes = new();
        foreach(var i in mesh.Triangles()) {
            indexes.Add((uint)i.a);
            indexes.Add((uint)i.b);
            indexes.Add((uint)i.c);
        }

        MeshGL result = new(verts.ToArray(), indexes.ToArray());
        return Manifold.Create(result);
    }

    public static DMesh3 ToDMesh(this Manifold manifold, bool want_normals = true) {
        DMesh3 result = new(want_normals);

        for (ulong i = 0; i < manifold.MeshGL.VerticesPropertiesLength; i += 3) {
            result.AppendVertex(new Vector3d {
                x = manifold.MeshGL.VerticesProperties[i],
                y = manifold.MeshGL.VerticesProperties[i + 1],
                z = manifold.MeshGL.VerticesProperties[i + 2]
            });
        }

        for (ulong i = 0; i < manifold.MeshGL.TriangleLength; i += 3) {
            result.AppendTriangle((new Index3i {
                a = (int)manifold.MeshGL.TriangleVertices[i],
                b = (int)manifold.MeshGL.TriangleVertices[i + 1],
                c = (int)manifold.MeshGL.TriangleVertices[i + 2]
            }));
        }

        return result;
    }
}
