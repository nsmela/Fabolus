using Fabolus.Core.Extensions;
using g3;
using static MR.DotNet;
using MeshNormals = g3.MeshNormals;

namespace Fabolus.Core.Meshes;

public class MeshModel {
    public DMesh3 Mesh { get; set; } = new DMesh3();

    // Public Static Functions

    public static MeshModel Copy(MeshModel mesh) {
        var result = new DMesh3();
        result.Copy(mesh.Mesh);
        return new MeshModel(result);
    }

    public static async Task<MeshModel> FromFile(string filepath) {
        var mesh = new DMesh3(await Task.Factory.StartNew(() => StandardMeshReader.ReadMesh(filepath)), false, true);
        return new MeshModel(mesh);
    }

    // Public Functions

    public void ApplyTransform(double x, double y, double z, double w) =>
        MeshTransforms.Rotate(Mesh, Vector3d.Zero, new Quaterniond(x, y, z, w));

    
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

    public double Volume {
        get {
            if (Mesh is null || Mesh.VertexCount == 0) { return 0.0; }

            var volumeAndArea = MeshMeasurements.VolumeArea(Mesh, Mesh.TriangleIndices(), Mesh.GetVertex);
            return volumeAndArea.x / 1000;
        }
    }

    public IEnumerable<double[]> Vectors() => Mesh.Vertices().Select(v => new double[] { v.x, v.y, v.z });

    public IEnumerable<int> Triangles() {
        foreach (var tri in Mesh.Triangles()) {
            yield return tri.a;
            yield return tri.b;
            yield return tri.c;
        }
    }

    public IEnumerable<double[]> Normals() {
        MeshNormals.QuickCompute(Mesh);

        return Enumerable.Range(0, Mesh.VertexCount).Select(i => Mesh.GetVertexNormal(i).ToArray());
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
