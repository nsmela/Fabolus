using Fabolus.Core.Extensions;
using Fabolus.Core.Smoothing;
using g3;
using gs;
using NetTopologySuite.Operation.Distance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Core.Meshes.MeshTools.MeshTools.Contouring;


namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools {

    public static IEnumerable<Vector3> OffsetPath3d(MeshModel model, IEnumerable<int> path, float distance) =>
        PolyLineOffset(model.Mesh, path, distance).Select(v => v.ToVector3());

    internal static Vector3d[] PolyLineOffset(DMesh3 mesh, IEnumerable<int> path, float distance) {
        Vertex[] vertices = GenerateVertices(mesh, path.ToArray());

        List<Vertex> loop = [];
        for( int i = 0; i < vertices.Length; i++) {
            Vector3d offset = vertices[i].Normal;
            offset.y = 0;
            offset.Normalize();

            loop.Add(vertices[i] with { Position = vertices[i].Position + offset * distance });
        }

        loop = RemoveReversedSegments(loop).ToList();
        Vector3d[] vectors = RemoveSharpCorners(loop).Select(v => v.Position).ToArray();
        int iterations = 1;
        for (int i = 0; i < iterations; i++) { vectors = LaplacianSmoothing(vectors).ToArray(); }
        return vectors;
    }

    internal static IEnumerable<Vertex> RemoveReversedSegments(IEnumerable<Vertex> vertices) {
        Vertex[] loop = vertices.ToArray();
        bool was_cleaned = true;
        List<Vertex> cleaned = [];
        double max_angle = 60.0;
        while (was_cleaned) {
            was_cleaned = false;

            Vertex v0, v1;
            Vector3d dir;
            int count = loop.Length;
            for (int i = 0; i < count; i++) {
                v0 = loop[(i - 1 + count) % count];
                v1 = loop[i % count];

                dir = (v1.Position - v0.Position).Normalized;
                double angle = dir.AngleD(v1.Direction);
                if (angle > max_angle) { // good position
                    was_cleaned = true;
                    //cleaned.Add(v1 with { Position = v0.Position + (v1.Position - v0.Position) * 0.5 }); // move the point slightly back
                    continue;
                }

                cleaned.Add(v1);
            }
            loop = cleaned.ToArray();
            cleaned = [];
        }

        return loop;
    }

    internal static IEnumerable<Vertex> RemoveSharpCorners(IEnumerable<Vertex> vertices) {
        const double min_angle = 100.0;

        Vertex[] loop = vertices.ToArray();
        List<Vertex> cleaned = [];

        bool was_cleaned = true;
        while (was_cleaned) {
            was_cleaned = false;

            Vertex v0, v1, v2;
            Vector3d dir0, dir2;
            double angle = 0.0;
            int count = loop.Length;
            for (int i = 0; i < count; i++) {
                v0 = loop[(i - 1 + count) % count];
                v1 = loop[i % count];
                v2 = loop[(i + 1) % count];

                dir0 = (v0 - v1).Normalized;
                dir2 = (v2 - v0).Normalized;

                angle = dir0.AngleD(dir2);

                if (angle > min_angle) { // good position
                    cleaned.Add(v1);
                    continue;
                }

                was_cleaned = true;
            }

            loop = cleaned.ToArray();
            cleaned = [];
        }

        return loop;
    }

    public static Vector3[] LaplacianSmoothing(Vector3[] points, int iterations = 1) {
        Vector3d[] path = points.Select(v => v.ToVector3d()).ToArray();

        for (int i = 0; i < iterations; i++) {
            path = LaplacianSmoothing(path).ToArray();
        }

        return path.Select(v => v.ToVector3()).ToArray();
    }

    internal static IEnumerable<Vector3d> LaplacianSmoothing(Vector3d[] path) {
        List<Vector3d> results = [];

        Vector3d v, vN, vP, dir;

        for (int i = 0; i < path.Length; i++) {
            v = path[i];
            vN = path[(i - 1 + path.Count()) % path.Count()];
            vP = path[(i + 1) % path.Count()];
            dir = (vN - v) * 0.25;
            results.Add(v + dir);
            dir = (vP - v) * 0.25;
            results.Add(v + dir);

        }

        return results;
    }

    internal record struct Vertex(int Id, Vector3d Position, Vector3d Direction, Vector3d Normal) {
        public static implicit operator Vector3d(Vertex v) => v.Position;

        public static Vector3d operator +(Vertex v0, Vertex v1) => v0.Position + v1.Position;
        public static Vector3d operator -(Vertex v0, Vertex v1) => v0.Position + (-v1.Position);
    }
}

