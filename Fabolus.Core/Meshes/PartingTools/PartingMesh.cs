using Clipper2Lib;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.PartingTools;
public static partial class PartingTools {
    public static CuttingMeshResults GeneratePartingMesh(MeshModel model, List<int[]> parting_indices, double inner_offset, double outer_offset, double seperation_distance) {
        if (parting_indices.Count == 0) { throw new Exception("No indices for Generating Parting Mesh!"); }
        
        List<PartingMesh> partings = [];
        partings.Add(PartingMesh.Create(model.Mesh, parting_indices[0], inner_offset, outer_offset));
        
        if (parting_indices.Count > 1) {
            partings.Add(PartingMesh.CreateClosed(model.Mesh, parting_indices[1], inner_offset));
        }

        // TODO: use MeshLib instead for this
        // c++ code:
        //Mesh mesh;
        //mesh.addSeparateContours( { initContour} ); // add edge loop in mesh representing initial ring
        //auto newLoop = makeDegenerateBandAroundHole(mesh, EdgeId(0)); // create degenerate triangle fan around initial ring
        //for (int i = 0; i < newLoop.size(); ++i)
        //    mesh.points[mesh.topology.org(newLoop[i])] = initContour[i] + initContourNormal[i] * extensionAmount; // extend degenerated fan to desired size

        //var loops = new MeshBoundaryLoops(parting.Mesh);
        // merging mutliple cutting meshes
        MeshEditor editor = new(new DMesh3());
        foreach (PartingMesh parting in partings) {
            editor.AppendMesh(parting.Mesh);
        }

        return new CuttingMeshResults {
            PartingPaths = parting_indices.Select(p => p.Select(i => model.Mesh.GetVertex(i).ToVector3()).ToArray()).ToList(),
            PartingIndices = parting_indices,
            CuttingMesh = new MeshModel(editor.Mesh),
            InnerPath = [],
            Model = model,
            OuterPath = partings[0].Vertices.Select(v => v.Position.ToVector3()).ToArray(),
        }; 
    }

    internal record struct Vertex(int Id, Vector3d Position, Vector3d Direction, Vector3d Normal) {
        public static implicit operator Vector3d(Vertex v) => v.Position;

        public static Vector3d operator +(Vertex v0, Vertex v1) => v0.Position + v1.Position;
        public static Vector3d operator -(Vertex v0, Vertex v1) => v0.Position + (-v1.Position);
    }

    internal record PartingMesh {
        public DMesh3 Mesh;
        public Vertex[] Vertices { get; private set; }
        public Vertex[] InnerVertices { get; private set; }

        public PartingMesh(Vertex[] vertices) {
            // populate mesh with starting vertices
            Mesh = new();
            for (int i = 0; i < vertices.Length; i++) {
                vertices[i] = vertices[i] with { Id = Mesh.AppendVertex(vertices[i]) };
            }

            InnerVertices = vertices;

            for (int i = 0; i < 3; i++) { 
                Vertices = vertices;
            } 
        }

        public void Offset(double distance, int iterations = 5) {
            Vertex[] vertices = OffsetVerts(Vertices, distance);
            for (int i = 0; i < iterations; i++) {
                vertices = AverageVertices(vertices);
            }
            StitchVerts(vertices);
        }

        public static PartingMesh CreateClosed(DMesh3 mesh, int[] path_indices, double inner_offset, int iterations = 5) {
            // convert the selected mesh vectors into a vertex array
            Vertex[] vertices = GenerateVertices(mesh, path_indices);

            // smooth the path
            for (int i = 0; i < iterations; i++) {
                vertices = AverageVertices(vertices);
            }
            vertices = LaplacianSmoothing(vertices);

            Vertex[] inner_vertices = OffsetVertNormals(vertices, -4 * Math.Abs(inner_offset));
            PartingMesh parting = new(inner_vertices);

            Vector3d min = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue);
            Vector3d max = new Vector3d(double.MinValue, double.MinValue, double.MinValue);
            foreach (Vertex v in parting.Vertices) {
                if (v.Position.x > max.x) { max.x = v.Position.x; }
                if (v.Position.x < min.x) { min.x = v.Position.x; }
                if (v.Position.y > max.y) { max.y = v.Position.y; }
                if (v.Position.y < min.y) { min.y = v.Position.y; }
                if (v.Position.z > max.z) { max.z = v.Position.z; }
                if (v.Position.z < min.z) { min.z = v.Position.z; }
            }
            Vector3d origin = (max - min) / 2 + min;
            int origin_id = parting.Mesh.AppendVertex(origin);

            MeshEditor editor = new(parting.Mesh);
            editor.AddTriangleFan_OrderedVertexLoop(origin_id, parting.Vertices.Select(v => v.Id).ToArray());
            return parting;
        }

        public static PartingMesh Create(DMesh3 mesh, int[] path_indices, double inner_offset, double outer_offset) {
            // convert the selected mesh vectors into a vertex array
            Vertex[] vertices = GenerateVertices(mesh, path_indices);

            // smooth the path
            vertices = AverageVertices(vertices);
            vertices = AverageVertices(vertices);
            vertices = LaplacianSmoothing(vertices);

            Vertex[] inner_vertices = OffsetVerts(vertices, -1 * Math.Abs(inner_offset));
            PartingMesh parting = new(inner_vertices);
            double distance = outer_offset;
            while (distance > 5.0) {
                parting.Offset(5.0);
                distance -= 5.0;
            }
            parting.Offset(distance);

            return parting;
        }

        private void StitchVerts(Vertex[] outer) {
            int nA = Vertices.Length;
            int nB = outer.Length;

            // align the two loops
            int closest = -1;
            double min_dist = double.MaxValue;
            double distance = 0.0;
            Vector3d v0, v1;
            for (int i = 0; i < nB; i++) {
                v0 = Vertices[0].Position;
                v1 = outer[i].Position;
                distance = (v1 - v0).LengthSquared;
                if (distance > min_dist) { continue; }

                closest = i;
                min_dist = distance;
            }

            // 'rotate' polyB to align
            int skip = closest % nB;
            int take = nB - outer.Skip(skip).Count();
            Vertex[] new_vertices = [.. outer.Skip(skip), .. outer.Take(take)];

            // add verts to mesh
            int index;
            Vertex vert;
            DMesh3 mesh = new(Mesh);

            for (int i = 0; i < nA; i++) {
                vert = Vertices[i];
                index = mesh.AppendVertex(vert.Position);
                Vertices[i] = vert with { Id = index };
            }

            for (int i = 0; i < nB; i++) {
                vert = new_vertices[i];
                index = mesh.AppendVertex(vert.Position);
                new_vertices[i] = vert with { Id = index };
            }

            List<int> a_indices = Vertices.Select(v => v.Id).ToList();
            List<int> b_indices = new_vertices.Select(v => v.Id).ToList();

            int a = 0, b = 0;
            double a_dist = double.MaxValue, b_dist = double.MaxValue;
            double a_angle = 0.0, b_angle = 0.0;
            while (a < nA && b < nB) {
                int a0 = a_indices[a % nA];
                int a1 = a_indices[(a + 1) % nA];
                int b0 = b_indices[b % nB];
                int b1 = b_indices[(b + 1) % nB];

                Vector3d vA0 = mesh.GetVertex(a0);
                Vector3d vA1 = mesh.GetVertex(a1);
                Vector3d vB0 = mesh.GetVertex(b0);
                Vector3d vB1 = mesh.GetVertex(b1);

                a_dist = mesh.GetVertex(a1).Distance(mesh.GetVertex(b0));
                a_angle = (vA1 - vA0).AngleD(vB0 - vA0);
                b_dist = mesh.GetVertex(b1).Distance(mesh.GetVertex(a0));
                b_angle = (vA0 - vB0).AngleD(vB1 - vB0);

                if (a_angle < b_angle) {
                    mesh.AppendTriangle(a1, a0, b0);
                    a++;
                }
                else {
                    mesh.AppendTriangle(a0, b0, b1);
                    b++;
                }
            }

            while (a < nA) {
                int a0 = a_indices[a % nA], a1 = a_indices[(a + 1) % nA], b0 = b_indices[b % nB];
                mesh.AppendTriangle(a1, a0, b0);
                a++;
            }

            while (b < nB) {
                int a0 = a_indices[a % nA], b0 = b_indices[b % nB], b1 = b_indices[(b + 1) % nB];
                mesh.AppendTriangle(a0, b0, b1);
                b++;
            }

            Vertices = outer;
            Mesh = mesh;
        }

    }

    internal static Vertex[] GenerateVertices(DMesh3 mesh, int[] path) {
        List<Vertex> vertices = new(path.Length); // to optimize by not needing to resize while adding

        int count = path.Length;
        for (int i = 0; i < count; i++) {
            // remove y component to normal, normalize it, and multiply by desired distance to get vector offset
            int vId = path[i];
            Vector3d position = mesh.GetVertex(vId);
            int prev_vId = path[(i - 1 + count) % count]; // allows wrapping around a closed loop
            Vector3d prev_pos = mesh.GetVertex(prev_vId);

            if (position.Distance(prev_pos) < 0.1) {
                continue; // too close, skip it
            }

            // get the averaged normal
            int next_vId = path[(i + 1) % count];
            Vector3d normal = mesh.GetVertexNormal(prev_vId) + mesh.GetVertexNormal(vId) + mesh.GetVertexNormal(next_vId);
            normal /= 3;
            normal = normal.Normalized;

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

    internal static Vertex[] OffsetVerts(Vertex[] verts, double distance) {
        Vertex[] results = new Vertex[verts.Length];
        for (int i = 0; i < verts.Length; i++) {
            Vector3d offset = new Vector3d(verts[i].Normal);
            offset.y = 0;
            offset.Normalize();

            var position = verts[i].Position + offset * distance;

            if (verts[i].Position.y != position.y) {
                throw new Exception();
            }

            results[i] = verts[i] with { Position = position };

        }

        results = RemoveReversedSegments(results).ToArray();
        results = RemoveSharpCorners(results).ToArray();

        return results.ToArray();
    }

    internal static Vertex[] OffsetVertNormals(Vertex[] verts, double distance) {
        Vertex[] results = new Vertex[verts.Length];
        for (int i = 0; i < verts.Length; i++) {
            Vector3d offset = new Vector3d(verts[i].Normal);
            var position = verts[i].Position + offset * distance;

            if (verts[i].Position.y != position.y) {
                throw new Exception();
            }

            results[i] = verts[i] with { Position = position };

        }

        results = RemoveReversedSegments(results).ToArray();
        results = RemoveSharpCorners(results).ToArray();

        return results.ToArray();
    }

    internal static Vertex[] AverageVertices(Vertex[] vertices) {
        List<Vertex> result = [];

        Vector3d v0, v1, v2;
        int count = vertices.Length;
        for (int i = 0; i < count; i++) {
            v0 = vertices[(i - 1 + count) % count].Position;
            v1 = vertices[i];
            v2 = vertices[(i + 1) % count];

            Vector3d v = (v0 + v1 + v2) / 3;
            result.Add(vertices[i] with { Position = v });
        }
        return result.ToArray();
    }

    internal static Vertex[] LaplacianSmoothing(Vertex[] vertices) {
        List<Vertex> results = [];

        int count = vertices.Length;
        Vertex v0, v1, v2, vA, vB;
        for (int i = 0; i < count; i++) {
            v0 = vertices[(i - 1 + count) % count]; // previous, looping
            v1 = vertices[i]; // current
            v2 = vertices[(i + 1) % count]; // next, looping

            vA = vertices[i] with { 
                Position = (v1 - v0) * 0.25 + v1,
                Normal = ((v0.Normal + v1.Normal + v2.Normal) / 3).Normalized
            };
            vA.Normal = new Vector3d(vA.Normal.x, 0, vA.Normal.z);

            vB = vertices[i] with {
                Position = (v1 - v2) * 0.25 + v1,
                Normal = ((v0.Normal + v1.Normal + v2.Normal) / 3).Normalized
            };
            vB.Normal = new Vector3d(vB.Normal.x, 0, vB.Normal.z);

            results.Add(vA);
            results.Add(vB);
        }

        return results.ToArray();
    }

    internal static double PathLength(Vertex[] vertices) {
        double length = vertices.Last().Position.Distance(vertices.First().Position);
        int count = vertices.Count() - 1;
        for (int i = 1; i < count; i++) {
            length += vertices[i].Position.Distance(vertices[i - 1]);
        }

        return length;
    }
}

public record struct CuttingMeshParams(
    MeshModel Bolus,
    MeshModel Mould,
    MeshModel Model,
    float InnerOffset = 0.1f,
    float OuterOffset = 25.0f,
    double MeshDepth = 0.1,
    double TwistThreshold = 0.0f
);

public record struct CuttingMeshResults {
    public List<int[]> PartingIndices = [];
    public float GapDistance = 0.1f;
    public List<Vector3[]> PartingPaths = [];
    public Vector3[] InnerPath = [];
    public Vector3[] OuterPath = [];
    public MeshModel Model;
    public MeshModel Mould;
    public MeshModel CuttingMesh;
    public MeshModel PositivePullMesh;
    public MeshModel NegativePullMesh;
    public MeshError[] Errors = [];
    public CuttingIntersection[] Intersections = [];

    public CuttingMeshResults() { }
} 

public record struct CuttingIntersection {
    public bool IsClosed;
    public Vector3[] Points;

    public CuttingIntersection() {
        IsClosed = false;
        Points = [];
    }
}

