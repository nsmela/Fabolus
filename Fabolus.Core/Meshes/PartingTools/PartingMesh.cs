using Clipper2Lib;
using Fabolus.Core.Extensions;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.PartingTools;
public static partial class PartingTools {
    public static Result<MeshModel> GeneratePartingMesh(MeshModel model, int[] parting_indices, double inner_offset, double outer_offset) {
        PartingMesh parting = PartingMesh.Create(model.Mesh, parting_indices, inner_offset, outer_offset);
        return new MeshModel(parting.Mesh);
    }

    internal record PartingMesh {
        public DMesh3 Mesh;
        public Vertex[] Vertices { get; private set; }

        public PartingMesh(Vertex[] vertices) {
            // populate mesh with starting vertices
            Mesh = new();
            for (int i = 0; i < vertices.Length; i++) {
                vertices[i] = vertices[i] with { Id = Mesh.AppendVertex(vertices[i]) };
            }

            Vertices = vertices;
        }

        public void Offset(double distance) {
            Vertex[] vertices = OffsetVerts(Vertices, distance);
            StitchVerts(vertices);
        }

        public static PartingMesh Create(DMesh3 mesh, int[] path_indices, double inner_offset, double outer_offset) {
            Vertex[] vertices = GenerateVertices(mesh, path_indices);
            Vertex[] inner_vertices = OffsetVerts(vertices, -1 * Math.Abs(inner_offset));

            PartingMesh parting = new(inner_vertices);
            parting.Offset(inner_offset);
            parting.Offset(outer_offset);
            parting.Offset(outer_offset);

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
            DMesh3 mesh = Mesh;

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

            while (a <= nA) {
                int a0 = a_indices[a % nA], a1 = a_indices[(a + 1) % nA], b0 = b_indices[b % nB];
                mesh.AppendTriangle(a1, a0, b0);
                a++;
            }

            while (b <= nB) {
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

        for (int i = 0; i < path.Length; i++) {
            // remove y component to normal, normalize it, and multiply by desired distance to get vector offset
            int vId = path[i];
            Vector3d position = mesh.GetVertex(vId);
            int prev_vId = path[(i - 1 + path.Length) % path.Length]; // allows wrapping around a closed loop
            Vector3d prev_pos = mesh.GetVertex(prev_vId);

            if (position.Distance(prev_pos) < 0.1) {
                continue; // too close, skip it
            }

            Vector3d normal = mesh.GetVertexNormal(vId);
            Vector3d direction = position - prev_pos; // the direction from the previous point to this one
            vertices.Add(new Vertex { Id = vId, Position = position, Direction = direction, Normal = normal });
        }

        return vertices.ToArray();
    }

    internal static Vertex[] OffsetVerts(Vertex[] verts, double distance) {
        Vertex[] results = new Vertex[verts.Length];
        for (int i = 0; i < verts.Length; i++) {
            Vector3d offset = new Vector3d(verts[i].Normal);
            offset.y = 0;
            offset.Normalize();

            results[i] = verts[i] with { Position = verts[i] + offset * distance };
        }

        results = RemoveReversedSegments(results).ToArray();
        results = RemoveSharpCorners(results).ToArray();

        return results.ToArray();
    }

}

public record struct CuttingMeshParams(
    MeshModel Model,
    float InnerOffset = 0.1f,
    float OuterOffset = 25.0f,
    double MeshDepth = 0.1,
    double TwistThreshold = 0.0f
);

public record struct CuttingMeshResults {
    public int[] PartingIndices = [];
    public Vector3[] PartingPath = [];
    public Vector3[] InnerPath = [];
    public Vector3[] OuterPath = [];
    public MeshModel Model;
    public MeshModel PolylineMesh;
    public MeshModel CuttingMesh;
    public MeshModel PositivePullMesh;
    public MeshModel NegativePullMesh;
    public MeshError[] Errors = [];

    public CuttingMeshResults() { }
} 

