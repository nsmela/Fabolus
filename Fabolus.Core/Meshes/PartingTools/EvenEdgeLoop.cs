using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;

public class EvenEdgeLoop {

    public EvenEdgeLoop(IEnumerable<Vector3d> path, int number_of_segments) {
        double totalLength = 0.0;
        List<Vector3d> points = path.ToList();

        List<Vector3d> z_intersections = [];
        List<int> z_indexes = [];

        // Calculate the total length of the path
        Vector3d v0, v1;
        for(int i = 0; i < points.Count; i++) {
            v0 = points[i];
            v1 = points[(i + 1) % points.Count];
            totalLength += v0.Distance(v1);

            // both points are above or below the z=0 plane, skip
            if (v0.z > 0 && v1.z > 0 || v0.z < 0 && v1.z < 0) { continue; }
            
            // find starting point on z 0 plane
            z_indexes.Add(i);

            double t = v0.z / (v0.z - v1.z);
            Vector3d intersection = v0 + (v1 - v0) * t;
            z_intersections.Add(intersection);
        }

        // find the highest x z intersection for our start point
        double max_x = double.MinValue;
        int index = -1;
        for(int i = 0; i < z_intersections.Count; i++) { 
            if (z_intersections[i].x < max_x) { continue; }

            max_x = z_intersections[i].x;
            index = i;
        }

        Vector3d z_intersection = z_intersections[index];
        int z_index = z_indexes[index];

        // the path will be set to rotate counter-clockwise around y positive
        // reorganize the path to start at the z inersection point
        List<Vector3d> new_path = [z_intersection];

        // determine if the path is counter-clockwise or clockwise based on the first two points
        // start at the z intersection point for consistency
        points = points.Skip(z_index).ToList();
        points.AddRange(points.Take(z_index)); // wrap around to the start

        // check the direction of the path around the y axis
        if ( points[z_index].z < points[z_index + 1].z)
        {
            points.Reverse(); // reverse the path if it is clockwise
        }

        points.Insert(0, z_intersection); // insert the z intersection point at the start

        // generate the even segments along the path
        // starting with z_intersection
        double segmentLength = totalLength / (double)number_of_segments;
        double distance_traveled = 0.0;
        int current_segement = 0;
        double segment_length = 0.0;
        List<Vector3d> even_path = [];
        // v0, v1 already declared above
        while(distance_traveled < totalLength) {
            v0 = points[current_segement % points.Count];
            v1 = points[(current_segement + 1) % points.Count];
        }
    }
}
