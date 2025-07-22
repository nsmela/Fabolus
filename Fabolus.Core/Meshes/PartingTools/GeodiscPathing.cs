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
        public int[] triangles; // ordered list of triangles in wedge
        public int[] edges; // edge opposite centre vertex in the matching triangle index
        public double min_angle;

        public Wedge(DMesh3 mesh, int v0, int v1, int v2) {
            this.a = v0;
            this.b = v1;
            this.c = v2;
            triangles = [];
            edges = [];

            // determine the starting point for calculations
            int eId = mesh.FindEdge(v0, v1);
            Index4i edge = mesh.GetEdge(eId); // starting edge

            int tId = edge.c;
            Index3i triangle = mesh.GetTriangle(tId); // starting triangle
            int vId = GetOpposingTriangleV(mesh, tId, v0, v1); // vId, v1 and tId is used to find the next triangle

            // collect the triangles surrounding the vertex, ordered. Direction doesn't matter
            List<int> tris = [];
            List<int> outer_edges = []; // edges that are on the outer boundry of the wedge
            outer_edges.Add( mesh.GetTriEdge(tId, v0)); // edge id of the triangle that is opposite the centre vertex
            while (!tris.Contains(tId)) {
                tris.Add(tId);
                tId = GetOpposingTriangle(mesh, v1, vId, tId); // get the other triangle from the shared edge
                vId = GetOpposingTriangleV(mesh, tId, v1, vId);
                outer_edges.Add(mesh.GetTriEdge(tId, v0));
            }

            // calculate angles for each outside boundry
            var tris_list = tris.ToList();

            double total_angle = 0.0;
            List<int> list = [v0];
            // int tId is reused from earlier
            // int triangle is used from before
            for (int i = 0; i < tris_list.Count; i++) {
                tId = tris_list[i];
                triangle = mesh.GetTriangle(tId);
                if (triangle.array.Any(v => v == v2)) { break; }

                // need to ensure this is correct
                // TODO: convert to rads, leave in degrees for now for easier troubleshooting
                var angle = GetTriangleAngle(mesh, tId, v1);
                total_angle += angle;
                list.Add(tId);

            }

            // determine which set of triangles are valid: the ones we scanned or the ones remaining
            if (total_angle < 180.0) {
                // this is a valid wedge, store the indexes
                triangles = list.ToArray();
                edges = outer_edges.Take(list.Count).ToArray();
                min_angle = total_angle;
            } else {
                // this is not a valid wedge, store the opposite side
                triangles = tris_list.Skip(list.Count).ToArray();
                edges = outer_edges.Skip(list.Count).ToArray();
                min_angle = 360.0 - total_angle;
            }

        }
    }

    private static double GetOuterBoundryAngle(DMesh3 mesh, int t0, int t1, int v0) {
        int a, b, c;
        Index3i tri0 = mesh.GetTriangle(t0);
        Index3i tri1 = mesh.GetTriangle(t1);

        if (tri0.a != v0 && tri1.array.Any(v => v == tri0.a)) {
            b = tri0.a;
            a = tri0.a != v0 ? tri0.a : tri0.c;
        }
        else if (tri0.b != v0 && tri1.array.Any(v => v == tri0.b)) {
            b = tri0.b;
            a = tri0.b != v0 ? tri0.b : tri0.c;
        } else if (tri0.c != v0 && tri1.array.Any(v => v == tri1.c)) {
            b = tri0.c;
            a = tri0.c != v0 ? tri0.c : tri0.a;
        } else {
            throw new Exception("Invalid triangle configuration for outer boundry angle calculation.");
        }
        c = tri1.array.Where(v => v != v0 && v != a).FirstOrDefault();

        return GetAngleBetweenVectors(mesh, a, b, c);

    }

    private static double GetTriangleAngle(DMesh3 mesh, int tId, int origin_vertex) {
        Index3i triangle = mesh.GetTriangle(tId);
        Vector3d v0 = mesh.GetVertex(triangle.a);
        Vector3d v1 = mesh.GetVertex(triangle.b);
        Vector3d v2 = mesh.GetVertex(triangle.c);
        Vector3d v1_v0 = v1 - v0;
        Vector3d v1_v2 = v1 - v2;
        return v1_v0.AngleD(v1_v2);
    }

    private static int GetOpposingTriangle(DMesh3 mesh, int a, int b, int triangle_id) {
        int edge_id = mesh.FindEdgeFromTri(a, b, triangle_id);
        Index4i edge = mesh.GetEdge(edge_id);
        if (edge.c != triangle_id) { return edge.c; } 
        else { return edge.d; }
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
        Vector3d v1_v0 = vec1 - vec0;
        Vector3d v1_v2 = vec1 - vec2;
        return v1_v0.AngleD(v1_v2);
    }

    public sealed class GeodiscPathing {
        private double[] _edge_lengths;
        private int[] _original_path;
        private List<int> _path;
        private bool _dirty = true; // flag to determine if FinalCompute needs to be recomputed

        public GeodiscPathing(DMesh3 mesh, int[] path) {
            _edge_lengths = new double[path.Length];// preset collection size for optimization

            // calculate edge lengths
            Vector3d v0, v1;
            for (int i = 0; i < path.Length - 1; i++) {
                v0 = mesh.GetVertex(path[i]);
                v1 = mesh.GetVertex(path[i + 1]);
                _edge_lengths[i] = v0.Distance(v1);
            }

            _original_path = path;
            _path = _original_path.ToList();

            Wedge wedge = new(mesh, path[0], path[1], path[2]);
        }

        public void Compute() {
            _dirty = true; // mark as dirty to recompute
            // create a wedge for each abc vertex in path

            // for each wedge:
            // for each wedge triangle, check outer boundry angle
            // if lower than pi, continue. already optimal
            // if higher than pi, flip the edge by adding the vert for the flipped triangle edge (mesh.GetEdgeOpposingV?)

            // when done, add v2 to the path (unless already added by the last triangle flip)
        }

        // splits the triangles on the mesh, setting the path and mesh for retreival
        private void FinalCompute() {
            if (!_dirty) { return;}
        }

        // converts the path to a list of Vector3d points
        // also splits triangles on the mesh to map the 
        public Vector3d[] GetPath() {
            FinalCompute();


            return [];
        }

        public DMesh3 Mesh() {
            FinalCompute();

            return new();
        }

        // check angle formed by path at each vertex, all angles > pi implies geodesic
        // flip edges where this is not true

        // flip out subroutine
        // where path makes angle < pi, flip the edge
        // if outer angle is less than pi, flip the edge

        // stopping threshold to prevent loops from converging

        // can use to generate bezier curves

    }
}
