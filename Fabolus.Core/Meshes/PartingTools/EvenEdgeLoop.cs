using g3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;

public static class EvenEdgeLoop {

    public static List<Vector3d> Generate(IEnumerable<Vector3d> path, int number_of_segments) {
        List<Vector3d> points = path.ToList();
        List<Vector3d> z_intersections = [];
        List<int> z_indexes = [];

        // collect z intersections
        Vector3d v0, v1;
        for(int i = 0; i < points.Count; i++) {
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
        for(int i = 0; i < z_intersections.Count; i++) { 
            if (z_intersections[i].x < max_x) { continue; }

            max_x = z_intersections[i].x;
            index = i;
        }

        Vector3d z_intersection = z_intersections[index];
        int z_index = z_indexes[index];

        // the path will be set to rotate counter-clockwise around y positive
        // reorganize the path to start at the z inersection point

        points = [..points.Skip(z_index), .. points.Take(z_index)];

        // determine if the path is counter-clockwise or clockwise based on the first two points
        // start at the z intersection point for consistency
        // check the direction of the path around the y axis
        if ( points[z_index].z > points[(z_index + 1) % points.Count].z)
        {
            points.Reverse(); // reverse the path if it is clockwise
        }

        points.Insert(0, z_intersection); // insert the z intersection point at the start

        // generate the stored lengths
        List<double> lengths = [0.0];
        double cumlative_length = 0.0;
        for(int i = 1; i < points.Count; i++) {
            v0 = points[i - 1];
            v1 = points[i];
            cumlative_length += v0.Distance(v1);
            lengths.Add(cumlative_length);
        }

        // generate the even segments along the path
        // starting with z_intersection
        double segment_spacing = cumlative_length / number_of_segments;
        List<Vector3d> sample_points = [z_intersection];
        double target_distance = segment_spacing;
        int seg_index = 0;

        for (int i = 1; i < number_of_segments; i++) {
            // iterate until at the right vertex based on needed distance
            while (seg_index < lengths.Count - 1 && lengths[seg_index] < target_distance) {
                seg_index++;
            }

            double start_distance = lengths[seg_index - 1];
            double end_distance = lengths[seg_index];
            double t = (target_distance - start_distance) / (end_distance - start_distance);

            v0 = points[seg_index];
            v1 = points[(seg_index + 1) % points.Count];

            Vector3d sample_point = Vector3d.Lerp(v0, v1, t);
            sample_points.Add(sample_point);

            target_distance += segment_spacing;
        }

        // TODO DEBUGGING
        for (int i = 1; i < sample_points.Count; i++) {
            v0 = sample_points[i - 1];
            v1 = sample_points[i];
            double distance = v0.Distance(v1);
            if (distance != segment_spacing) {
                Debug.WriteLine($"Segment {i} distance: {distance}, expected: {segment_spacing}");
            }
        }

        return sample_points;
    }
}
