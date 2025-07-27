using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools {

    /// <summary>
    /// Processes a closed loop of 3d vertices for referencing later
    /// </summary>
    /// <param name="path"></param>
    public class EdgeLoopReference {

        public EdgeLoopReference(IEnumerable<Vector3d> path) {
            List<Vector3d> points = path.ToList();
            List<Vector3d> z_intersections = [];
            List<int> z_indexes = [];

            // collect z intersections
            Vector3d v0, v1;
            for (int i = 0; i < points.Count; i++) {
                v0 = points[i];
                v1 = points[(i + 1) % points.Count];

                // both points are above or below the z=0 plane, skip
                if (v0.z > 0 && v1.z > 0 || v0.z < 0 && v1.z < 0) { continue; }

                // find starting point on z 0 plane
                z_indexes.Add(i);

                double t = v0.z / (v0.z - v1.z);
                Vector3d intersection = v0 + (v1 - v0) * t;
                z_intersections.Add(intersection);
            }

            // find max_x z intersection for our start point
            double max_x = double.MinValue;
            int index = -1;
            for (int i = 0; i < z_intersections.Count; i++) {
                if (z_intersections[i].x < max_x) { continue; }

                max_x = z_intersections[i].x;
                index = i;
            }

            if (index < 0) {
                // no z intersections found, return empty list
                throw new Exception("No z intersections found in the path. Ensure the path crosses the z=0 plane.");
            }

            Vector3d z_intersection = z_intersections[index];
            int z_index = z_indexes[index];
        }
    }
}
