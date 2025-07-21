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
        public int[] min_tris;

        public Wedge(DMesh3 mesh, int v0, int v1, int v2) {
            this.a = v0;
            this.b = v1;
            this.c = v2;
            min_tris = [];

            // sort ordered_boundry so each vertex is connected to the next by edge
            int eId = mesh.FindEdge(v0, v1);
            Index4i edge = mesh.GetEdge(eId);
            int tId = edge.c;
            Index3i triangle = mesh.GetTriangle(tId);
            int vId = GetOpposingTriangleV(mesh, tId, v0, v1);
            HashSet<int> processed_triangles = [];
            processed_triangles.Add(tId);

            HashSet<int> ordered_boundry = [vId];
            while(vId != v0) {
                tId = GetOpposingTriangle(mesh, v1, vId, tId); // get the other triangle from the shared edge
                vId = GetOpposingTriangleV(mesh, tId, v1, vId);
                ordered_boundry.Add(vId);
            }

            // calculate angles
            var ordered_boundry_list = ordered_boundry.ToList();
            int index_start = ordered_boundry_list.IndexOf(v0);
            int index_end = ordered_boundry_list.IndexOf(v2);
            ordered_boundry_list.AddRange(ordered_boundry_list.Take(index_start)); // rotate so v0 is first // TODO this is messy
            ordered_boundry_list.RemoveRange(0, index_start);
            double total_angle = 0.0;
            Vector3d v_0, v_1, v_2;
            List<int> list = [];
            for (int i = 1; i < ordered_boundry.Count - 1; i++) {
                if (i == v2) { break; }

                v_0 = mesh.GetVertex(ordered_boundry_list[i - 1]);
                v_1 = mesh.GetVertex(ordered_boundry_list[i]);
                v_2 = mesh.GetVertex(ordered_boundry_list[i + 1]);

                Vector3d v1_v0 = v_1 - v_0;
                Vector3d v1_v2 = v_1 - v_2;

                // need to ensure this is correct
                ftotal_angle += v1_v0.AngleR(v1_v2);
                list.Add(ordered_boundry_list[i]);
            }

            if (total_angle < Math.PI) {
                // this is a valid wedge, store the indexes
                min_tris = list.ToArray();
            } else {
                // this is not a valid wedge, store the opposite side
                min_tris = [];
            }

        }
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

    public class GeodiscPathing {
        private double[] _edge_lengths;


        public GeodiscPathing(DMesh3 mesh, int[] path) {
            _edge_lengths = new double[path.Length];// preset collection size for optimization

            // calculate edge lengths
            Vector3d v0, v1;
            for (int i = 0; i < path.Length - 1; i++) {
                v0 = mesh.GetVertex(path[i]);
                v1 = mesh.GetVertex(path[i + 1]);
                _edge_lengths[i] = v0.Distance(v1);
            }

            Wedge wedge = new(mesh, path[0], path[1], path[2]);
        }

        public void Compute() {

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
