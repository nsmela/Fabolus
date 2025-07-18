using Fabolus.Core.Extensions;
using g3;
using gs;
using System.CodeDom;
using System.Windows.Media.Media3D;
using static MR.DotNet;
using MeshNormals = g3.MeshNormals;

namespace Fabolus.Core.Meshes;

public class MeshModel {
    public DMesh3 Mesh { get; set; } = new DMesh3();
    internal Mesh _mesh { get; set; }

    // Public Static Functions

    public static MeshModel Copy(MeshModel model) {
        var mesh = new DMesh3();
        mesh.Copy(model.Mesh);
        return new MeshModel(mesh);
    }

    public static async Task<MeshModel> FromFile(string filepath) {
        //var mesh = new DMesh3(await Task.Factory.StartNew(() => StandardMeshReader.ReadMesh(filepath)), false, true);
        var mesh = MeshLoad.FromAnySupportedFormat(filepath);
        return new MeshModel(mesh);
    }

    public static async Task ToFile(string filepath, MeshModel model) {
        //var mesh = model.Mesh;
        //StandardMeshWriter.WriteMesh(filepath, mesh, WriteOptions.Defaults);
        MeshSave.ToAnySupportedFormat(model, filepath);
    }

    // Public Functions

    public void ApplyRotation(double x, double y, double z, double w) =>
        MeshTransforms.Rotate(Mesh, Vector3d.Zero, new Quaterniond(new Vector3d(x, y, z), w));

    public void ApplyTranslation(double x, double y, double z) =>
        MeshTransforms.Translate(Mesh, new Vector3d(x, y, z));

    public bool IsEmpty() => Mesh is null || Mesh.TriangleCount == 0;

    public double Height => Mesh.CachedBounds.Max.z;

    /// <summary>
    /// Returns the vertices of a triangle as an array of doubles. 
    /// </summary>
    /// <param name="tId"></param>
    /// <returns>3x3 double array as a simple 9 double array</returns>
    public double[] GetTriangleAsDoubles(int tId) {
        Index3i triangle = Mesh.GetTriangle(tId);
        return new double[] {
            Mesh.GetVertex(triangle.a).x, Mesh.GetVertex(triangle.a).y, Mesh.GetVertex(triangle.a).z,
            Mesh.GetVertex(triangle.b).x, Mesh.GetVertex(triangle.b).y, Mesh.GetVertex(triangle.b).z,
            Mesh.GetVertex(triangle.c).x, Mesh.GetVertex(triangle.c).y, Mesh.GetVertex(triangle.c).z
        };
    }

    public int[] GetTriangleNeighbours(int tId) => Mesh.GetTriNeighbourTris(tId).array;

    public double[] GetVector(int vId) =>
        new double[] {
            Mesh.GetVertex(vId).x,
            Mesh.GetVertex(vId).y,
            Mesh.GetVertex(vId).z
        };

    public IEnumerable<int> GetBorderEdgeLoop(int[] region_ids) {
        //select the region
        var region = new MeshRegionBoundaryLoops(Mesh, region_ids, true);
        var loops = region.Loops;

        int last_id = -1;
        Index4i edge;
        foreach (var eId in loops[0].Edges) {
            edge = Mesh.GetEdge(eId);
            if (edge.a == last_id){ last_id = edge.b; }
            else { last_id = edge.a; }

            yield return last_id;
        }

    }

    public IEnumerable<double[]> GetVertices(int[] vert_ids) {
        foreach(int vId in vert_ids) {
            var vertex = Mesh.GetVertex(vId);
            yield return new double[] { vertex.x, vertex.y, vertex.z };
        }
    }

    public IEnumerable<double[]> GetBorderVerts(int[] region_ids) {
        //select the region
        var region = new MeshRegionBoundaryLoops(Mesh, region_ids, true);
        var loops = region.Loops;

        foreach(var id in loops[0].Vertices) {
            var vertex = Mesh.GetVertex(id);
            yield return new double[] { vertex.x, vertex.y, vertex.z };
        }
    }

    public IEnumerable<(double, double, double)> NormalVectors() {
        for (int i = 0; i < Mesh.VertexCount; i++) {
            var vector = Mesh.GetVertexNormal(i);
            yield return (vector.x, vector.y, vector.z);
        }
    }

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

    public static MeshModel Combine(MeshModel[] models) {
        MeshEditor editor = new(new DMesh3());

        // assume meshes do not overlap and can be appended directly
        foreach (DMesh3 model in models.Select(m => m.Mesh)) {
            editor.AppendMesh(model);
        }

        MeshAutoRepair repair = new(editor.Mesh);
        repair.Apply();

        return new(repair.Mesh);
    }

    public MeshModel(Mesh mesh) {
        Mesh = mesh.ToDMesh();
        _mesh = mesh; // Store the original Mesh for further operations if needed
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

    // combine two MeshModels into one
    public MeshModel(IEnumerable<MeshModel> models, float distance = 0.1f) {
        DMesh3[] meshes = models.Select(m => m.Mesh).ToArray();
        if (meshes.Length < 2) {
            throw new ArgumentException("At least two MeshModels are required to combine them.");
        }

        distance = Math.Abs(distance); // Ensure distance is non-negative

        MeshEditor editor = new(meshes[0]);
        DMesh3 mesh2 = new(meshes[1]);
        MeshTransforms.Translate(mesh2, new Vector3d(0, distance, 0));
        editor.AppendMesh(mesh2);

        Mesh = new DMesh3(editor.Mesh);
    }


    // Operators

    public static implicit operator DMesh3(MeshModel model) => model.Mesh;
    public static explicit operator MeshModel(DMesh3 mesh) => new(mesh);

    public static implicit operator Mesh(MeshModel model) => model.Mesh.ToMesh();
    public static explicit operator MeshModel(Mesh mesh) => new(mesh);

    public static bool IsNullOrEmpty(MeshModel model) => model is null || model.Mesh is null || model.Mesh.VertexCount == 0;

}
