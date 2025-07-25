using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;
//ref: https://www.youtube.com/watch?v=DbNEsryLULE

public static partial class PartingTools {

    private record struct Wedge {
        public int a, b, c;
        public int[] boundry_points; // vertices connected to the centre vertex

        public Wedge(DMesh3 mesh, int v0, int v1, int v2) {
            this.a = v0;
            this.b = v1;
            this.c = v2;

            // collect the boundry points
            int vId = v2; // start with the first vertex connected to v1
            int eId = mesh.FindEdge(vId, v1);
            if (eId == DMesh3.InvalidID) {
                throw new Exception();
            }
            int tId = mesh.GetEdgeT(eId).a; // starting triangle from the edge, dont care which

            bool found_v0 = false; // flag to determine if we found v0 in the outer points
            List<int> outer_points = []; // points that are on the outer boundry of the wedge, starting at v0

            while (!outer_points.Contains(vId)) {
                outer_points.Add(vId); // add the current vertex to the outer points

                // get the next vertex in the boundry
                tId = GetOpposingTriangle(mesh, v1, vId, tId); // get the next triangle id
                vId = GetOpposingTriangleV(mesh, tId, v1, vId); // get the next vertex id

                if (vId == v0) { found_v0 = true; } // the previous vertex might not be connected from edge flipping
            }

            // check 
            if (!found_v0) {
                // we did not find v0, so we need to add it to the outer points
                // do two vertices connect to it?
                var results = outer_points
                    .Where(v => mesh.FindEdge(v, v0) != DMesh3.InvalidID)
                    .ToArray();
                if (results.Length != 2) {
                    var neighbours = mesh.VtxVerticesItr(v0).ToArray();
                    throw new Exception($"Wedge {v0}, {v1}, {v2} does not have two vertices connected to v0, found {results.Length} vertices.");
                }
            }

            // calculate angles for each outside boundry
            double total_angle = 0.0;
            int end_index = outer_points.IndexOf(v0) + 1; // end at the  wedge end, with offset zero indexing
            for (int i = 1; i < end_index; i++) {
                // need to ensure this is correct
                // TODO: convert to rads, leave in degrees for now for easier troubleshooting
                var angle = GetAngleBetweenVectors(mesh, outer_points[i - 1], v1, outer_points[i]);
                total_angle += angle;

            }

            // determine which set of triangles are valid: the ones we scanned or the ones remaining
            if (total_angle < 180.0) {
                // this is a valid wedge, store the indexes
                boundry_points = outer_points.Take(end_index).ToArray();
            } else {
                // this is not a valid wedge, store the opposite side
                outer_points.Add(v2);
                boundry_points = outer_points.Skip(end_index - 1).Reverse().ToArray();
            }
            if (boundry_points.Length < 3) {
                //throw new Exception($"Wedge {v0}, {v1}, {v2} does not have enough boundry points, found {boundry_points.Length} points.");
            }
        }

        public double GetOuterBoundryAngle(DMesh3 mesh, int vId) {
            int v0 = boundry_points[vId - 1];
            int v1 = boundry_points[vId];
            int v2 = boundry_points[vId + 1];

            Vector3d v1_v0 = mesh.GetVertex(v1) - mesh.GetVertex(v0);
            Vector3d v1_v2 = mesh.GetVertex(v1) - mesh.GetVertex(v2);

            // return value
            return v1_v0.AngleD(v1_v2);
        }

        public int[] FlipoutPath(DMesh3 mesh) {
            List<int> path = [boundry_points[0]]; // use first point
            for (int i = 1; i < boundry_points.Length - 1; i++) {
                // if the angle is less than 180, we can skip the current point by "flipping" the edge
                double angle = GetOuterBoundryAngle(mesh, i);
                if (angle > 180.0) {
                    path.Add(boundry_points[i]); // do not flip, add the current point to the path
                } else {
                    path.Add(boundry_points[i + 1]); // flip the edge, add the next point to the path
                }
            }

            // compare path distances
            double original_length = GetPathDistance(mesh, [a, b, c]);
            double fliped_path = GetPathDistance(mesh, path.ToArray());

            if (original_length < fliped_path) { return [b]; } // no change needed

            //Remesh(mesh, path.ToArray()); // remesh the path
            path.RemoveAt(0);
            if (path.Count > 0) {
                path.RemoveAt(path.Count - 1); // remove the last point
            } 

            return path.ToArray(); // return the path without the first and last points
        }

        // split edges and triangles to make the new path valid along the mesh
        // TODO: this is not implemented yet, need to figure out how to split the triangles and edges
        private void Remesh(DMesh3 mesh, int[] path) {
            // assume v0, v1 and v2 are valid points and along existing edges
            // path starts with v0, ends with v2
            for (int i = 1; i < path.Length; i++) {
                // edge exists between this point and previous point?
                int eId = mesh.FindEdge(path[i - 1], path[i]);
                if (eId != DMesh3.InvalidID) { continue; } // edge exists, no work needed

                // edge does not exist, we need to create it
                // there is an edge between v1 and path[i] and an edge for v1 and path[i - 1]
                // we need the two triangle ids
                // split their shared edge along where the new edge should show
                DMesh3.EdgeFlipInfo info = new();
                var mesh_result = mesh.FlipEdge(path[i - 1], path[i], out info); // flip the edge to create a new triangle
            }

        }
    }

    private static int GetOpposingTriangle(DMesh3 mesh, int a, int b, int triangle_id) {
        int edge_id = mesh.FindEdgeFromTri(a, b, triangle_id);
        if (edge_id == DMesh3.InvalidID) {
            edge_id = mesh.FindEdge(a, b);
        }

        Index2i edge = mesh.GetEdgeT(edge_id);
        if (edge.a != triangle_id) { return edge.a; } 
        else { return edge.b; }
    }

    private static int GetOpposingTriangleV(DMesh3 mesh, int tId, int a, int b) {
        Index3i triangle = mesh.GetTriangle(tId);
        if (triangle.a != a && triangle.a != b) { return triangle.a; }
        if (triangle.b != a && triangle.b != b) { return triangle.b; }

        return triangle.c;
    }

    private static double GetAngleBetweenVectors(DMesh3 mesh, int v0, int v1, int v2) {
        Vector3d vec0 = mesh.GetVertex(v0);
        Vector3d vec1 = mesh.GetVertex(v1);
        Vector3d vec2 = mesh.GetVertex(v2);
        Vector3d v1_v0 = (vec1 - vec0).Normalized;
        Vector3d v1_v2 = (vec1 - vec2).Normalized;
        return v1_v0.AngleD(v1_v2);
    }

    private static double GetPathDistance(DMesh3 mesh, params int[] path) {
        double total = 0.0;
        if (path.Length < 2) { return total; } // insufficient path length
        for (int i = 1; i < path.Length; i++) {
            total += mesh.GetVertex(path[i - 1]).Distance(mesh.GetVertex(path[i]));
        }

        return total;
    }

    public sealed class GeodiscPathing {
        private int[] _original_path;
        private List<int> _path;
        private DMesh3 _mesh;
        private bool _dirty = true; // flag to determine if FinalCompute needs to be recomputed

        public GeodiscPathing(DMesh3 mesh, IEnumerable<int> path) {
            int count = path.Count();
            _mesh = mesh;

            _original_path = path.ToArray();
            _path = _original_path.ToList();
        }

        public void Compute() {
            _dirty = true; // mark as dirty to recompute

            List<int> new_path = [];
            int v0, v1, v2;
            Wedge wedge;
            for (int i = 1; i < _path.Count - 1; i++) {
                // create a wedge for each abc vertex in path
                v0 = _path[i - 1];
                v1 = _path[i];
                v2 = _path[(i + 1) % _path.Count];

                if (v0 == v1 || v1 == v2) { continue; } // the path has duplicate vertices, skip to clear them

                wedge = new Wedge(_mesh, v0, v1, v2);
                new_path.AddRange(wedge.FlipoutPath(_mesh)); // add the flipped path to the new path

            }

            _path = new_path;
            
        }

        // splits the triangles on the mesh, setting the path and mesh for retreival
        private void FinalCompute() {
            if (!_dirty) { return;}
        }

        // converts the path to a list of Vector3d points
        // also splits triangles on the mesh to map the 
        public IEnumerable<int> Path() {
            FinalCompute(); // calculates the rest, if needed

            return _path;
        }

        public DMesh3 Mesh() {
            FinalCompute(); // calculates the rest, if needed

            return _mesh;
        }

        // stopping threshold to prevent loops from converging

        // can use to generate bezier curves

    }
}
