using Fabolus.Core.Extensions;
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

    public IEnumerable<(double, double, double)> NormalVectors() {
        for (int i = 0; i < Mesh.VertexCount; i++) {
            var vector = Mesh.GetVertexNormal(i);
            yield return (vector.x, vector.y, vector.z);
        }
    }

    public double[] Offsets => new double[] {
        Mesh.CachedBounds.Center.x, 
        Mesh.CachedBounds.Center.y, 
        Mesh.CachedBounds.Center.z 
    };

    public IEnumerable<int> TriangleIndexes() {
        foreach (var tri in Mesh.Triangles()) {
            yield return tri.a;
            yield return tri.b;
            yield return tri.c;
        }
    }
 
    public IEnumerable<(double, double, double)> TriangleVectors() => Mesh.Vertices().Select(v => (v.x, v.y, v.z));

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
    
    // Operators

    public static implicit operator DMesh3(MeshModel model) => model.Mesh;
    public static explicit operator MeshModel(DMesh3 mesh) => new(mesh);

    public static implicit operator Mesh(MeshModel model) => model.Mesh.ToMesh();
    public static explicit operator MeshModel(Mesh mesh) => new(mesh);
}
