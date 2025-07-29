using Fabolus.Core.Extensions;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools {
    public static Result<MeshModel> JoinPolylines(Vector3[] inner, Vector3[] outer) {
        Vector3d[] polyA = inner.Select(v => v.ToVector3d()).ToArray();
        Vector3d[] polyB = outer.Select(v => v.ToVector3d()).ToArray();
        var mesh = JoinPolylines(polyA, polyB);
        return new MeshModel(mesh);
    }

    internal static DMesh3 JoinPolylines(Vector3d[] inner, Vector3d[] outer) {
        int nA = inner.Length;
        int nB = outer.Length;

        // align the two loops
        int closest = -1;
        double min_dist = double.MaxValue;
        double distance = 0.0;
        for (int i = 0; i < nB; i++) {
            distance = (inner[0] - outer[i]).LengthSquared;
            if (distance > min_dist) { continue; }

            closest = i;
            min_dist = distance;
        }

        // 'rotate' polyB to align
        List<Vector3d> new_poly = new(outer.Count());
        new_poly.AddRange(outer.Skip(closest + 1));
        new_poly.AddRange(outer.Take(closest));
        outer = new_poly.ToArray();
        nB = outer.Length;

        // add verts to mesh
        DMesh3 mesh = new();
        List<int> a_indices = new(nA); // pre-set size for efficiency
        List<int> b_indices = new(nB);
        foreach (Vector3d v in inner) {
            a_indices.Add(mesh.AppendVertex(v));
        }

        foreach (Vector3d v in outer) {
            b_indices.Add(mesh.AppendVertex(v));
        }

        int a = 0, b = 0;
        double a_dist = 0.0, b_dist = 0.0;
        while (a < nA || b < nB) {
            int a0 = a_indices[a % nA], a1 = a_indices[(a + 1) % nA], b0 = b_indices[b % nB], b1 = b_indices[(b + 1) % nB];

            a_dist = mesh.GetVertex(a1).DistanceSquared(mesh.GetVertex(b0));
            b_dist = mesh.GetVertex(b1).DistanceSquared(mesh.GetVertex(a0));
            if (a_dist < b_dist) { // b / 8 is to prevent infinate looping
                mesh.AppendTriangle(a1, a0, b0);
                a++;
            }
            else {
                mesh.AppendTriangle(a0, b0, b1);
                b++;
            }
        }

        return mesh;
    }

    public static IEnumerable<Vector3> OffsetPath3d(MeshModel model, IEnumerable<int> path, float distance) =>
        PolyLineOffset(model.Mesh, path, distance).Select(v => v.ToVector3());

    internal static Vector3d[] PolyLineOffset(DMesh3 mesh, IEnumerable<int> path, float distance) {
        Vector3f[] normals = path.Select(i => mesh.GetVertexNormal(i)).ToArray();
        Vector3d[] points = path.Select(i => mesh.GetVertex(i)).ToArray();

        Vector3d[] results = new Vector3d[points.Length]; // optmize by pre-setting the expected length
        for (int i = 0; i < points.Length; i++) {
            // remove y component to normal, normalize it, and multiply by desired distance to get vector offset
            results[i] = points[i] + new Vector3f(normals[i].x, 0.0, normals[i].z).Normalized * distance;
        }

        // create edge loop
        List<Vertex> loop = [];
        Vector3d position = results.Last();
        Vector3d direction = (points.First() - points.Last()).Normalized;
        loop.Add(new Vertex() {
            Id = 0,
            Position = position,
            Direction = direction,
        });

        double min_position_distance = 0.1;
        for (int i = 1; i < points.Length; i++) {

            position = results[i];
            if (position.DistanceSquared(results[i - 1]) < min_position_distance) {
                continue; // positions are too close
            }

            direction = (points[i] - points[i - 1]).Normalized;

            loop.Add(new Vertex() {
                Id = i,
                Position = position,
                Direction = direction,
            });
        }

        bool was_cleaned = true;
        List<Vertex> cleaned = [];
        double max_angle = 45.0;
        while (was_cleaned) {
            was_cleaned = false;

            Vertex v0, v1;
            Vector3d dir;
            for (int i = 1; i <= loop.Count; i++) {
                v0 = loop[i - 1];
                v1 = loop[i % loop.Count];

                dir = (v1.Position - v0.Position).Normalized;
                double angle = dir.AngleD(v1.Direction);
                if (angle > max_angle) { // good position
                    was_cleaned = true;
                    continue;
                }

                cleaned.Add(v1);
            }
            loop = cleaned;
            cleaned = [];
        }

        return loop.Select(l => l.Position).ToArray();
    }

    private record struct Vertex(int Id, Vector3d Position, Vector3d Direction);

}

