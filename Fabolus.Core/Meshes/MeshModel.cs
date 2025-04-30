using Fabolus.Core.Extensions;
using g3;
using System.Windows.Media.Media3D;
using static MR.DotNet;
using MeshNormals = g3.MeshNormals;

namespace Fabolus.Core.Meshes;

public class MeshModel {
    public DMesh3 Mesh { get; set; } = new DMesh3();
    private List<Quaterniond> Rotations { get; set; } = [];

    // Public Static Functions

    public static MeshModel Copy(MeshModel model) {
        var mesh = new DMesh3();
        mesh.Copy(model.Mesh);
        return new MeshModel(mesh);
    }

    public static async Task<MeshModel> FromFile(string filepath) {
        var mesh = new DMesh3(await Task.Factory.StartNew(() => StandardMeshReader.ReadMesh(filepath)), false, true);
        return new MeshModel(mesh);
    }

    public static async Task ToFile(string filepath, MeshModel model) {
        var mesh = model.Mesh;
        StandardMeshWriter.WriteMesh(filepath, mesh, WriteOptions.Defaults);
    }

    // Public Functions

    public void ApplyRotation(double x, double y, double z, double w) =>
        MeshTransforms.Rotate(Mesh, Vector3d.Zero, new Quaterniond(new Vector3d(x, y, z), w));

    
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

    public MeshModel(IEnumerable<Vector3D> vectors, IList<int> triangleIndexes) {
        Mesh = new DMesh3();

        foreach (var vector in vectors) {
            Mesh.AppendVertex(new Vector3d(vector.X, vector.Y, vector.Z));
        }

        for(int i = 0; i < triangleIndexes.Count(); i += 3) {
            Mesh.AppendTriangle(triangleIndexes[i], triangleIndexes[i + 1], triangleIndexes[i + 2]);
        }

    }
    
    // Operators

    public static implicit operator DMesh3(MeshModel model) => model.Mesh;
    public static explicit operator MeshModel(DMesh3 mesh) => new(mesh);

    public static implicit operator Mesh(MeshModel model) => model.Mesh.ToMesh();
    public static explicit operator MeshModel(Mesh mesh) => new(mesh);

    public static bool IsNullOrEmpty(MeshModel model) => model is null || model.Mesh is null || model.Mesh.VertexCount == 0;

}
