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

    public static Result<MeshModel> PartingMesh(IEnumerable<Vector3> points, double offset) {
        // create the countours used to make the mesh cutter
        var outer_contour = GenerateContour(points.Select(p => new Vector2d(p.X, p.Z)), offset);
        if (outer_contour.IsFailure) { return outer_contour.Errors; }
        Polygon2d outer_polygon = new(outer_contour.Data.Select(v => new Vector2d(v.x, v.y)));

        var inner_contour = GenerateContour(points.Select(p => new Vector2d(p.X, p.Z)), -1.5);
        if (inner_contour.IsFailure) { return inner_contour.Errors; }
        Polygon2d inner_polygon = new(inner_contour.Data.Select(v => new Vector2d(v.x, v.y)));
        inner_polygon.Reverse(); // ensure the inner polygon is reversed to be a hole

        // even out the loops so they're consistent
        var input_path = EvenEdgeLoop.Generate(points.Select(p => new Vector3d(p.X, p.Y, p.Z)), 100);

        // triangulate the contours
        PlanarSolid2d planar = new();
        planar.SetOuter(CurveUtils2.Convert(outer_polygon), false);
        planar.AddHole(CurveUtils2.Convert(inner_polygon));

        TriangulatedPolygonGenerator generator = new() {
            Clockwise = true,
            Polygon = planar.Convert(2.0, 2.0, 0.2)
        };

        DMesh3 result = new();
        try {
            result = generator.Generate().MakeDMesh();

            // rotating the mesh. Tried MeshTransforms.Rotate but no effect
            int id;
            Vector3d v;
            foreach (int vId in result.VertexIndices()) {
                v = result.GetVertex(vId);
                result.SetVertex(vId, new Vector3d(v.x, 0, v.y));
            }

            // progress offset on inner boundry
            List<Vector3d> path = points.Select(p => new Vector3d(p.X, p.Y, p.Z)).ToList();
            var path_offsets = new ContourOffsetGraph(path);

            foreach(EdgeLoop loop in new MeshBoundaryLoops(result)) {
                if (loop.VertexCount < 3) { continue; } // skip degenerate loops
                var offsets = new ContourOffsetGraph(result, loop);

                for (int i = 0; i < loop.VertexCount; i++) {
                    int index = (offsets.StartIndex + i) % loop.VertexCount;
                    float progress = (float)offsets.Distances[index];
                    double y_offset = path_offsets.GetYOffset(progress);

                    int vId = loop.Vertices[index];
                    Vector3d vector = result.GetVertex(vId) + Vector3d.AxisY * y_offset;
                    result.SetVertex(vId, vector);
                }
            }

        } catch (Exception ex) {
            return new MeshError($"Failed to triangulate parting mesh: {ex.Message}");
        }

        return new MeshModel(result);
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

        /// <summary>
        /// Returns the YOffset for the percentage along this path, from 0.0 to 1.0
        /// </summary>
        public double GetYOffset(float progress) {
            if (progress < 0.0 || progress > 1.0) {
                throw new ArgumentOutOfRangeException(nameof(progress), "Progress must be between 0.0 and 1.0");
            }

            // find the relevant section
            // TODO: make this more efficient
            int index = -1;
            for (int i = 0; i < Distances.Count(); i++) {
                if (Distances[i] >= progress) {
                    return _vertices[i].y; // return the Y value at this index
                }
            }

            // if we reach here, the progress is beyond the end of the path
            return 1.0;
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