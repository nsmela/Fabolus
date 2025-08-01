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



    public record struct CuttingMeshParams(
        MeshModel Model,
        float InnerOffset = 0.1f,
        float OuterOffset = 25.0f,
        double MeshDepth = 0.1,
        double TwistThreshold = 0.0f
    );

    public record struct CuttingMeshResults {
        public int[] PartingIndices = [];
        public Vector3[] PartingPath = [];
        public Vector3[] InnerPath = [];
        public Vector3[] OuterPath = [];
        public MeshModel Model;
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
        var parting_line = GeneratePartingLine(parameters.Model);
        
        DMesh3 mesh = parameters.Model;

        var outer_path = PolyLineOffset(mesh, parting_line, parameters.OuterOffset);
        var inner_path = PolyLineOffset(mesh, parting_line, -Math.Abs(parameters.InnerOffset)); // ensures offset goes inwards

        DMesh3 result = new();// JoinPolylines(inner_path.ToArray(), outer_path.ToArray());

        // extrude the mesh face
        MeshExtrudeMesh extrude = new(result) {
            ExtrudedPositionF = (v, n, vId) => v + Vector3d.AxisY * parameters.MeshDepth,
        };
        extrude.Extrude();

        // repair the mesh if needed
        MeshAutoRepair repair = new(extrude.Mesh);
        repair.Apply();

        return new() {
            Model = parameters.Model,
            PartingIndices = parting_line.ToArray(),
            PartingPath = parting_line.Select(v => parameters.Model.Mesh.GetVertex(v).ToVector3()).ToArray(),
            OuterPath = outer_path.Select(v => v.ToVector3()).ToArray(),
            InnerPath = inner_path.Select(v => v.ToVector3()).ToArray(),
            PolylineMesh = new MeshModel(result),
            CuttingMesh = new MeshModel(repair.Mesh),
        };
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