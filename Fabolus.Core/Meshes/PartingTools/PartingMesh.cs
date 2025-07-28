using Clipper2Lib;
using Fabolus.Core.Extensions;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;
public static partial class PartingTools {

    public static Result<MeshModel> JoinPolylines(Vector3[] inner, Vector3[] outer) {
        Vector3d[] polyA = inner.Select(v => v.ToVector3d()).ToArray();
        Vector3d[] polyB = outer.Select(v => v.ToVector3d()).ToArray();
        var mesh = JoinPolylines(polyA, polyB);
        return new MeshModel(mesh);
    }

    internal static DMesh3 JoinPolylines(Vector3d[] inner, Vector3d[] outer) {
        int nA = inner.Length;
        int nB = outer.Length;

        // align the two loops
        int closest = -1;
        double min_dist = double.MaxValue;
        double distance = 0.0;
        for (int i = 0; i < nB; i++) {
            distance = (inner[0] - outer[i]).LengthSquared;
            if (distance > min_dist) { continue; }

            closest = i;
            min_dist = distance;
        }

        // 'rotate' polyB to align
        List<Vector3d> new_poly = new(outer.Count());
        new_poly.AddRange(outer.Skip(closest + 1));
        new_poly.AddRange(outer.Take(closest));
        outer = new_poly.ToArray();
        nB = outer.Length;

        // add verts to mesh
        DMesh3 mesh = new();
        List<int> a_indices = new(nA); // pre-set size for efficiency
        List<int> b_indices = new(nB);
        foreach (Vector3d v in inner) {
            a_indices.Add(mesh.AppendVertex(v));
        }

        foreach (Vector3d v in outer) {
            b_indices.Add(mesh.AppendVertex(v));
        }

        int a = 0, b = 0;
        double a_dist = 0.0, b_dist = 0.0;
        while (a < nA || b < nB) {
            int a0 = a_indices[a % nA], a1 = a_indices[(a + 1) % nA], b0 = b_indices[b % nB], b1 = b_indices[(b + 1) % nB];

            a_dist = mesh.GetVertex(a1).DistanceSquared(mesh.GetVertex(b0));
            b_dist = mesh.GetVertex(b1).DistanceSquared(mesh.GetVertex(a0));
            if (a_dist < b_dist) { // b / 8 is to prevent infinate looping
                mesh.AppendTriangle(a1, a0, b0);
                a++;
            } else {
                mesh.AppendTriangle(a0, b0, b1);
                b++;
            }
        }

        return mesh;
    }

    public record struct CuttingMeshParams(
        MeshModel Model,
        float InnerOffset = 0.1f,
        float OuterOffset = 25.0f,
        double MeshDepth = 0.1
    );

    public record struct CuttingMeshResults {
        public Vector3[] PartingPath = [];
        public Vector3[] InnerPath = [];
        public Vector3[] OuterPath = [];
        public MeshModel PolylineMesh;
        public MeshModel CuttingMesh;
        public MeshModel PositivePullMesh;
        public MeshModel NegativePullMesh;
        public MeshError[] Errors = [];

        public CuttingMeshResults() { }
    } 

    /// <summary>
    /// Generate a 3d mesh along another mesh's calculated parting line
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static CuttingMeshResults DualOffsetCuttingMesh(CuttingMeshParams parameters) {
        CuttingMeshResults results = new();
        var parting_line = GeneratePartingLine(parameters.Model);
        results = results with { PartingPath = parting_line.Select(v => parameters.Model.Mesh.GetVertex(v).ToVector3()).ToArray() };
        
        DMesh3 mesh = parameters.Model.Mesh;

        var outer_path = PolyLineOffset(mesh, parting_line, parameters.OuterOffset);
        results = results with { OuterPath = outer_path.Select(v => v.ToVector3()).ToArray() };

        var inner_path = PolyLineOffset(mesh, parting_line, -Math.Abs(parameters.InnerOffset)); // ensures offset goes inwards
        results = results with { InnerPath = inner_path.Select(v => v.ToVector3()).ToArray() };

        DMesh3 result = JoinPolylines(inner_path.ToArray(), outer_path.ToArray());
        results = results with { PolylineMesh = new MeshModel(result) };

        // extrude the mesh face
        MeshExtrudeMesh extrude = new(result) {
            ExtrudedPositionF = (v, n, vId) => v + Vector3d.AxisY * parameters.MeshDepth,
        };
        extrude.Extrude();

        // repair the mesh if needed
        MeshAutoRepair repair = new(extrude.Mesh);
        repair.Apply();

        return results with { CuttingMesh = new MeshModel(repair.Mesh) };
    }

    public static Result<MeshModel> EvenPartingMesh(IEnumerable<Vector3> points, double offset, double extrude_distance = 0.1) {
        // create a consistent reference for the inner and outer loops to reference for the y offset
        var even_path = EvenEdgeLoop.Generate(points.Select(p => new Vector3d(p.X, p.Y, p.Z)), points.Count());

        // create the contours used to make the mesh cutter
        // inner contour first to ensure good penetration of the model
        var inner_contour = GenerateContour(even_path.Select(p => new Vector2d(p.x, p.z)), -0.2);
        if (inner_contour.IsFailure) { return inner_contour.Errors; }
        var inner_even_loop = EvenEdgeLoop.Generate(inner_contour.Data.Select(v => new Vector3d(v.x, 0.0, v.y)), even_path.Count);

        var outer_contour = GenerateContour(even_path.Select(p => new Vector2d(p.x, p.z)), offset + 1.0);
        if (outer_contour.IsFailure) { return outer_contour.Errors; }
        var outer_even_loop = EvenEdgeLoop.Generate(outer_contour.Data.Select(v => new Vector3d(v.x, 0.0, v.y)), even_path.Count);

        if (outer_even_loop.Count != inner_even_loop.Count || outer_even_loop.Count != even_path.Count) {
            return new MeshError($"Outer and inner loops have different vertex counts: Path: {even_path.Count} Outer: {outer_even_loop.Count} != Inner: {inner_even_loop.Count}");
        }

        // add y offsets back to the loops
        for (int i = 0; i < even_path.Count; i++) {
            var v_y = Vector3d.AxisY * even_path[i].y;
            var v0 = outer_even_loop[i] + v_y;
            var v1 = inner_even_loop[i] + v_y;
            outer_even_loop[i] = v0;
            inner_even_loop[i] = v1;
        }

        // triangulate the space between the two loops
        DMesh3 mesh = new();
        
        List<int> outer_indexes = [];
        foreach(Vector3d v in outer_even_loop) {
            outer_indexes.Add(mesh.AppendVertex(v));
        }

        List<int> inner_indexes = [];
        foreach (Vector3d v in inner_even_loop) {
            inner_indexes.Add(mesh.AppendVertex(v));
        }

        MeshEditor editor = new(mesh);
        editor.StitchLoop(outer_indexes.ToArray(), inner_indexes.ToArray());

        // extrude the mesh face
        MeshExtrudeMesh extrude = new(editor.Mesh) {
            ExtrudedPositionF = (v, n, vId) => v + Vector3d.AxisY * extrude_distance,
        };
        extrude.Extrude();

        return new MeshModel(extrude.Mesh);
    }

    internal static Result<Vector2d[]> GenerateContour(IEnumerable<Vector2d> points, double offset) {
        IEnumerable<PointD> path = points.Select(v => new PointD(v.x, v.y)); // assuming pull direction of Y Positive

        // convert into a Clipper2 path
        PathsD paths = new() { new PathD(path) };
        PathsD inflated = Clipper.InflatePaths(paths, offset, JoinType.Round, EndType.Polygon);

        if (inflated is null || inflated.Count == 0) {
            return new MeshError($"Failed to create an inflated contour: no solution found");
        }

        return inflated[0].Select(p => new Vector2d(p.x, p.y)).ToArray();
    }

    private sealed class ContourOffsetGraph {
        public List<double> Distances;
        List<Vector3d> _vertices;
        public int StartIndex { get; set; }
        public double TotalLength { get; set; }

        public ContourOffsetGraph(DMesh3 mesh, EdgeLoop loop) {
            List<Vector3d> path = [];
            foreach (int vId in loop.Vertices) {
                path.Add(mesh.GetVertex(vId));
            }

            Setup(path);
        }

        public ContourOffsetGraph(List<Vector3d> path) {
            Setup(path);
        }

        private void Setup(List<Vector3d> path) {
            if (path.Count() < 2) { return; } // insufficient path length

            // create loop starting where the loop crosses z = 0 and negative x
            Vector3d starting_vector = Vector3d.Zero;

            TotalLength = 0.0;
            List<Vector3d> vertices = [];
            for (int i = 0; i < path.Count() - 1; i++) {
                var v0 = path[i];
                var v1 = path[i + 1];

                vertices.Add(v0);

                // does this section cross z zero?
                if (v0.z > 0 && v1.z > 0 || v0.z < 0 && v1.z < 0) { continue; }

                double t = v0.z / (v0.z - v1.z); // progress along segment where z = 0
                Vector3d intersection = v0 + t * (v1 - v0); // calculate intersection point

                // is this close enough to the starting point?
                if (starting_vector == Vector3d.Zero || intersection.x < starting_vector.x) {
                    StartIndex = i; // update the starting index
                    starting_vector = intersection; // update the starting vector if it's further left
                }

            }

            // reorganize the vertices to start at the intersection point
            _vertices = [starting_vector];
            _vertices.AddRange(vertices.Skip(StartIndex));
            _vertices.AddRange(vertices.Take(StartIndex + 1)); // wrap around to the start

            CheckClockwise();

            TotalLength = _vertices.Last().Distance(_vertices.First());
            for (int i = 1; i < _vertices.Count(); i++) {
                TotalLength += _vertices[i - 1].Distance(_vertices[i]); // accumulate total length
            }

            // set total length list as percentage
            Distances = [0.0];
            double distance = 0.0;
            for (int i = 1; i < _vertices.Count(); i++) {
                distance += _vertices[i - 1].Distance(_vertices[i]);
                Distances.Add(distance / TotalLength); // accumulate percentage
            }

        }

        // ensures the path is going a specific direction
        private void CheckClockwise() {
            if (_vertices[0].z < _vertices[1].z) { return; }

            // reverse the path if it's not going clockwise
            var start = _vertices[0];
            _vertices.RemoveAt(0);
            _vertices.Reverse();
            _vertices.Insert(0, start); // reinsert the starting point at the beginning
        }
    }
}