using Clipper2Lib;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;
public static partial class PartingTools {

    public static Result<MeshModel> EvenPartingMesh(IEnumerable<Vector3> points, double offset) {
        var even_path = EvenEdgeLoop.Generate(points.Select(p => new Vector3d(p.X, p.Y, p.Z)), 100);

        // create the contours used to make the mesh cutter
        var outer_contour = GenerateContour(even_path.Select(p => new Vector2d(p.x, p.z)), offset);
        if (outer_contour.IsFailure) { return outer_contour.Errors; }
        var outer_even_loop = EvenEdgeLoop.Generate(outer_contour.Data.Select(v => new Vector3d(v.x, 0.0, v.y)), 100);

        var inner_contour = GenerateContour(even_path.Select(p => new Vector2d(p.x, p.z)), -1.5);
        if (inner_contour.IsFailure) { return inner_contour.Errors; }
        var inner_even_loop = EvenEdgeLoop.Generate(inner_contour.Data.Select(v => new Vector3d(v.x, 0.0, v.y)), 100);

        // add y offsets back to the loops
        for (int i = 0; i < even_path.Count; i++) {
            outer_even_loop[i] += Vector3d.AxisY * even_path[i].y;
            inner_even_loop[i] += Vector3d.AxisY * even_path[i].y;
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
            ExtrudedPositionF = (v, n, vId) => v + Vector3d.AxisY * 1.0, // extrude upwards by 0.1 units
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