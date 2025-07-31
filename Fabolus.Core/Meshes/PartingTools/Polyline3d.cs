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

    public class PartingMesh {
        internal DMesh3 Mesh { get; private set; }
        internal List<EdgeLoop> Loops { get; private set; }

        internal static PartingMesh FromCurves(IEnumerable<Vector3d> inner_curve, IEnumerable<Vector3d> outer_curve)
        {
            int[] inner_indices = [];
            int[] outer_indices = [];
            DMesh3 mesh = JoinPolylines(inner_curve.ToArray(), outer_curve.ToArray(), out inner_indices, out outer_indices);

            return new PartingMesh
            {
                Mesh = mesh,
                Loops = [EdgeLoop.FromVertices(mesh, inner_indices), EdgeLoop.FromVertices(mesh, outer_indices)]
            };
        }
    }

    public static Result<MeshModel> JoinPolylines(Vector3[] start_path, IEnumerable<Vector3[]> paths) {
        Vector3d[] starting = start_path.Select(v => v.ToVector3d()).ToArray();

        List<Vector3d[]> pathing = paths
            .Select(v => v.Select(vv => vv.ToVector3d()).ToArray())
            .ToList();

        if (pathing.Count() == 0) { return new MeshError($" Joining polylines needs 1 or more paths. Submitted paths: {pathing.Count()}"); }

        MeshEditor editor = new(new DMesh3());
        DMesh3 mesh = JoinPolylines(starting, pathing[0]);
        editor.AppendMesh(mesh);

        for (int i = 1; i < pathing.Count(); i++) {
            mesh = JoinPolylines(pathing[i - 1], pathing[i]);
            editor.AppendMesh(mesh);
        }

        return new MeshModel(editor.Mesh);
    }

    internal static DMesh3 JoinPolylines(Vector3d[] inner, Vector3d[] outer)
    {
        int[] inner_indices = [];
        int[] outer_indices = [];

        return JoinPolylines(inner, outer, out inner_indices, out outer_indices);
    }

    internal static DMesh3 JoinPolylines(Vector3d[] inner, Vector3d[] outer, out int[] inner_indices, out int[] outer_indices) {
        inner_indices = []; 
        outer_indices = [];

        int nA = inner.Length;
        int nB = outer.Length;

        if (nA == 0 || nB == 0) { return new(); }

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
        double a_dist = double.MaxValue, b_dist = double.MaxValue;
        double a_angle = 0.0, b_angle = 0.0;
        while (a < nA && b < nB) {
            int a0 = a_indices[a % nA], a1 = a_indices[(a + 1) % nA], b0 = b_indices[b % nB], b1 = b_indices[(b + 1) % nB];

            a_dist = mesh.GetVertex(a1).Distance(mesh.GetVertex(b0));
            a_angle = (mesh.GetVertex(a1) - mesh.GetVertex(a0)).AngleD(mesh.GetVertex(b0) - mesh.GetVertex(a0));
            b_dist = mesh.GetVertex(b1).Distance(mesh.GetVertex(a0));
            b_angle = (mesh.GetVertex(a0) - mesh.GetVertex(b0)).AngleD(mesh.GetVertex(b1) - mesh.GetVertex(a0));

            if (a_angle < b_angle) { 
                mesh.AppendTriangle(a1, a0, b0);
                a++;
            }
            else {
                mesh.AppendTriangle(a0, b0, b1);
                b++;
            }
        }

        while(a < nA) {
            int a0 = a_indices[a % nA], a1 = a_indices[(a + 1) % nA], b0 = b_indices[b % nB];
            mesh.AppendTriangle(a1, a0, b0);
            a++;
        }

        while (b < nB) {
            int a0 = a_indices[a % nA], b0 = b_indices[b % nB], b1 = b_indices[(b + 1) % nB];
            mesh.AppendTriangle(a0, b0, b1);
            b++;
        }

        inner_indices = a_indices.ToArray();
        outer_indices = b_indices.ToArray();

        return mesh;
    }

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

    /// <summary>
    /// Generates consecutive path offsets seperated by the seg_distance
    /// </summary>
    /// <param name="model"></param>
    /// <param name="path"></param>
    /// <param name="distance"></param>
    /// <param name="seg_distance"></param>
    /// <returns></returns>
    public static IEnumerable<Vector3[]> OffsetPath3dSegmented(MeshModel model, IEnumerable<int> path, float distance, float seg_distance) {
        // if only one calculation is needed
        // TODO: should this return an error instead?
        if (distance < seg_distance) {
            return [OffsetPath3d(model, path, distance).ToArray()];
        }

        List<Vertex> original = GenerateVertices(model.Mesh, path.ToArray()).ToList();
        List<Vertex[]> results = [];
        List<Vertex> loop = original.ToList();
        List<Vertex> cleaned = [];

        int iterations = (int)(distance / seg_distance);
        double segment_distance = (double)(distance / iterations);
        for(int i = 1; i <= iterations; i++) {
            loop = OffsetVerts(loop, segment_distance);
            results.Add(loop.ToArray());
        }

        return results.Select(v => v.Select(vv => vv.Position.ToVector3()).ToArray());
    }

    internal static List<Vertex> OffsetVerts(List<Vertex> verts, double distance) {
        List<Vertex> results = [];
        foreach (Vertex v in verts) {
            Vector3d offset = new Vector3d(v.Normal);
            offset.y = 0;
            offset.Normalize();

            results.Add(v with { Position = v + offset * distance });
        }

        results = RemoveReversedSegments(results).ToList();
        results = RemoveSharpCorners(results).ToList();

        return results;
    }

    internal static Vertex[] GenerateVertices(DMesh3 mesh, int[] path) {
        List<Vertex> vertices = new(path.Length); // to optimize by not needing to resize while adding

        for (int i = 0; i < path.Length; i++) {
            // remove y component to normal, normalize it, and multiply by desired distance to get vector offset
            int vId = path[i];
            Vector3d position = mesh.GetVertex(vId);
            int prev_vId = path[(i - 1 + path.Length) % path.Length]; // allows wrapping around a closed loop
            Vector3d prev_pos = mesh.GetVertex(prev_vId);

            if (position.DistanceSquared(prev_pos) < 0.01) {
                continue; // too close, skip it
            }

            Vector3d normal = mesh.GetVertexNormal(vId);
            Vector3d direction = position - prev_pos; // the direction from the previous point to this one
            vertices.Add(new Vertex { Id = vId, Position = position, Direction = direction, Normal = normal });
        }

        return vertices.ToArray();
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

