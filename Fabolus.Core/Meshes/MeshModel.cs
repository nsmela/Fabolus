using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Meshes;
public class MeshModel {
    private DMesh3 Mesh { get; set; } = new DMesh3();

    public bool IsEmpty() => Mesh is null || Mesh.TriangleCount == 0;

    public IEnumerable<(double, double, double)> TriangleVectors() => Mesh.Vertices().Select(v => (v.x, v.y, v.z));
    public IEnumerable<(double, double, double)> NormalVectors() {
        for (int i = 0; i < mesh.VertexCount; i++) {
            var vector = mesh.GetVertexNormal(i);
            yield return (vector.a, vector.b, vector.c);
        }
    }

    public IEnumerable<(int, int, int)> TriangleIndices() => Mesh.TriangleIndices().Select(t => (t.a, t.b, t.c));

    public IEnumerable<int> TriangleIndexes() {
        foreach (var tri in mesh.Triangles()) {
            yield return tri.a;
            yield return tri.b;
            yield return tri.c;
        }
    }

    public double Volume {
        get {
            if (Mesh is null || Mesh.VertexCount == 0) { return 0.0; }

            var volumeAndArea = MeshMeasurements.VolumeArea(Mesh, Mesh.TriangleIndices(), Mesh.GetVertex);
            return volumeAndArea.x / 1000;
        }
    }

    // Constructors

    public MeshModel() { }

    public MeshModel(DMesh3 mesh) {
        Mesh = mesh;
    }
    public MeshModel(Mesh mesh) {
        Mesh = mesh.ToDMesh();
    }

    public MeshModel(IEnumerable<(double, double, double)> vectors, IEnumerable<(int, int, int)> triangleIndexes) {
        Mesh = new DMesh3();
        foreach (var vector in vectors) {
            Mesh.AppendVertex(new Vector3d(vector.Item1, vector.Item2, vector.Item3));
        }
        foreach (var triangle in triangleIndexes) {
            Mesh.AppendTriangle(triangle.Item1, triangle.Item2, triangle.Item3);
        }

    }
    // Conversion methods

    public static DMesh3 ToDMesh(this Mesh mesh) {
        DMesh3 result = new();

        foreach (var p in mesh.Points) {
            result.AppendVertex(new Vector3d(p.X, p.Y, p.Z));
        }
        foreach (var t in mesh.Triangulation) {
            result.AppendTriangle(t.v0.Id, t.v1.Id, t.v2.Id);
        }

        return result
    }

    public static Mesh ToMesh(this DMesh3 mesh) {
        List<MR.DotNet.Vector3f> verts = mesh.Vertices().Select(v => new MR.DotNet.Vector3f((float)v.x, (float)v.y, (float)v.z)).ToList();
        List<ThreeVertIds> tris = mesh.Triangles().Select(t => new ThreeVertIds(t.a, t.b, t.c)).ToList();
        return Mesh.FromTriangles(verts, tris);
    }
    
    // Operators

    public static implicit operator DMesh3(MeshModel model) => model.Mesh;
    public static explicit operator MeshModel(DMesh3 mesh) => new(mesh);

    public static implicit operator Mesh(MeshModel model) => model.Mesh.ToMesh();
    public static explicit operator MeshModel(Mesh model) => new(mesh);
}
