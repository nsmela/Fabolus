using g3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

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

        if (index <0) {
            // no z intersections found, return empty list
            throw new Exception("No z intersections found in the path. Ensure the path crosses the z=0 plane.");
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
        Vector3d current_pos = z_intersection;
        Vector3d previous = Vector3d.Zero;
        double target_distance = segment_spacing;
        int seg_index = 0;

        for(int i = 0; i < number_of_segments - 1; i++) { 
            if (!TryFindNextSphereIntersection(points, sample_points.Last(), previous, segment_spacing, ref seg_index, out Vector3d next_point)) {
                break; // no more points can be found
            }

            previous = current_pos;
            current_pos = next_point;
            sample_points.Add(next_point); // add the next point to the sample points
        }

        return sample_points;
    }

    public static bool TryFindNextSphereIntersection(
        List<Vector3d> path, Vector3d center, Vector3d previous, double radius,
        ref int segmentIndex, out Vector3d nextPoint) {

        nextPoint = Vector3d.Zero;
        double r2 = radius * radius;

        int maxSteps = 10;
        for (int step = 0; step < maxSteps; step++) {
            Vector3d a = path[segmentIndex];
            Vector3d b = path[(segmentIndex + 1) % path.Count];
            Vector3d d = b - a;
            Vector3d f = a - center;

            double A = d.Dot(d);
            double B = 2 * f.Dot(d);
            double C = f.Dot(f) - r2;

            double discriminant = B * B - 4 * A * C;

            if (discriminant < 0) {
                segmentIndex = (segmentIndex + 1) % path.Count;
                continue;
            }

            double sqrtD = Math.Sqrt(discriminant);
            double inv2A = 1 / (2 * A);
            double t1 = (-B - sqrtD) * inv2A;
            double t2 = (-B + sqrtD) * inv2A;

            // Only consider intersections ahead along the path
            if (t1 > 0 && t1 <= 1) {
                Vector3d hit = a + t1 * d;
                if (hit.Distance(previous) >= radius / 2) {
                    nextPoint = hit;
                    return true;
                }
            }

            if (t2 > 0 && t2 <= 1) {
                Vector3d hit = a + t2 * d;
                if (hit.Distance(previous) >= radius / 2) {
                    nextPoint = hit;
                    return true;
                }
            }
            
            // Move to next segment if not found
            segmentIndex = (segmentIndex + 1) % path.Count;
        }

        return false; // No valid forward intersection found
    }

}
