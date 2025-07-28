using Fabolus.Core.Extensions;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;

public class Polyline3d {
    public Vector3[] Points => _points.Select(v => v.ToVector3()).ToArray();
    private List<Vector3d> _points = [];

    public Polyline3d(IEnumerable<Vector3> path) {
        _points = path.Select(p => p.ToVector3d()).ToList();
    }

    internal Polyline3d(IEnumerable<Vector3d> vectors) {
        _points = vectors.ToList();
    }

    public Polyline3d Offset(double distance) {
        var result = PartingTools.OffsetPathInXZPlane(_points, distance);

        // clean up
        // smooth?

        return new Polyline3d(result);
    }

    public bool IsEmpty() => _points.Count == 0;
}

public static partial class PartingTools {
    public static IEnumerable<Vector3> OffsetPath(MeshModel model, IEnumerable<int> path, float distance) =>
        PolyLineOffset(model.Mesh, path, distance).Select(v => v.ToVector3());

    internal static Vector3d[] PolyLineOffset(DMesh3 mesh, IEnumerable<int> path, float distance) {
        Vector3f[] normals = path.Select(i => mesh.GetVertexNormal(i)).ToArray();
        Vector3d[] points = path.Select(i => mesh.GetVertex(i)).ToArray();

        Vector3d[] results = new Vector3d[points.Length]; // optmize by pre-setting the expected length
        for (int i = 0; i < points.Length; i++) {
            // remove y component to normal, normalize it, and multiply by desired distance to get vector offset
            results[i] = points[i] + new Vector3f(normals[i].x, 0.0, normals[i].z).Normalized * distance;
        }

        var info = new CleanupResults() { RemovedCount = int.MaxValue };
        while (!info.IsClean) {
            results = OffsetCleaup(results, out info);
        }
        
        return results.ToArray();
    }

    internal static List<Vector3d> OffsetPathInXZPlane(List<Vector3d> path, double offsetDistance) {
        int n = path.Count;
        if (n < 2)
            throw new ArgumentException("Path must contain at least 2 points.");

        var offsetPoints = new List<Vector3d>();

        // Compute tangents
        List<Vector3d> tangents = new List<Vector3d>();
        for (int i = 0; i < n - 1; i++)
            tangents.Add((path[i + 1] - path[i]).Normalized);
        tangents.Add(tangents[^1]);

        // Choose initial normal that's not aligned with tangent
        Vector3d t0 = tangents[0];
        Vector3d arbitrary = Vector3d.AxisY;
        if (Math.Abs(t0.Dot(arbitrary)) > 0.99)
            arbitrary = Vector3d.AxisX;

        Vector3d n0 = t0.Cross(arbitrary).Normalized;
        Vector3d b0 = t0.Cross(n0).Normalized;

        // Project binormal to XZ plane
        Vector3d b0xz = new Vector3d(b0.x, 0, b0.z).Normalized;
        Vector3d p0 = path[0] + b0xz * offsetDistance;
        p0.y = path[0].y; // preserve Y
        offsetPoints.Add(p0);

        Vector3d currentNormal = n0;

        for (int i = 1; i < n; i++) {
            Vector3d prevT = tangents[i - 1];
            Vector3d currT = tangents[i];

            Vector3d v = currT + prevT;
            if (v.LengthSquared == 0)
                v = prevT;
            v = v.Normalized;

            Vector3d projection = currentNormal - v * currentNormal.Dot(v);
            if (projection.LengthSquared < 1e-6) {
                // fallback normal
                projection = currT.Cross(Vector3d.AxisX);
                if (projection.LengthSquared < 1e-6)
                    projection = currT.Cross(Vector3d.AxisZ);
            }

            currentNormal = projection.Normalized;
            Vector3d binormal = currT.Cross(currentNormal).Normalized;

            // Project binormal to XZ and preserve Y
            Vector3d binormalXZ = new Vector3d(binormal.x, 0, binormal.z);
            if (binormalXZ.LengthSquared > MathUtil.ZeroTolerancef)
                binormalXZ = binormalXZ.Normalized;

            Vector3d offsetPoint = path[i] + binormalXZ * offsetDistance;
            offsetPoint.y = path[i].y; // ensure Y doesn't change
            offsetPoints.Add(offsetPoint);
        }

        return offsetPoints;
    }

    internal record struct CleanupResults(int RemovedCount) {
        public bool IsClean => RemovedCount == 0;
    }

    internal static Vector3d[] OffsetCleaup(IEnumerable<Vector3d> path, out CleanupResults info, double twist_threshold = 0.0) {
        int count = path.Count();
        if (count < 2) {
            info = new() { RemovedCount = 0 };
            return path.ToArray(); // nothing to offset
        }

        Vector3d[] points = path.ToArray();
        List<Vector3d> cleanedPoints = new List<Vector3d>();
        int removed_count = 0;
        for (int i = 0; i < count; i++) {
            Vector3d p0 = points[(i - 1 + count) % count]; // previous point
            Vector3d p1 = points[i]; // current point
            Vector3d p2 = points[(i + 1) % count]; // next point

            // check if the segment is twisted
            if (IsTwisted(p0, p1, p2, twist_threshold)) {
                removed_count++;
                continue;
            }

            cleanedPoints.Add(p1); // keep the current point if it's not twisted
        }

        info = new() { RemovedCount = removed_count };
        return cleanedPoints.ToArray();
    }
}

