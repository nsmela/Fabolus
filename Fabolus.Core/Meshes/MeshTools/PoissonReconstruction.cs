using Fabolus.Core.Extensions;
using g3;
using NetTopologySuite.GeometriesGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {
    // ref: https://hhoppe.com/poissonrecon.pdf


    internal static Result<MeshModel> PoissonReconstruction(
        DMesh3 mesh,
        int depth = 5,
        double scale = 1.0,
        int samplesPerNode = 10
    ) {

        return Result<MeshModel>.Fail(new MeshError("No implemented"));
    }

    private class Octree {
        public OctreeNode Root { get; set; }
        public int MaxPointsPerLeaf { get; set; } = 16;
        public int MaxDepth { get; set; } = 8;

        public Octree(DMesh3 mesh) {
            if (mesh.IsEmpty()) { throw new ArgumentException("Mesh cannot be empty.", nameof(mesh)); }

            // need to ensure octree is cubic
            double max_size = Math.Max(mesh.CachedBounds.Width, Math.Max(mesh.CachedBounds.Height, mesh.CachedBounds.Depth));
            Vector3d centre = mesh.CachedBounds.Center;
            Vector3d half = Vector3d.One * (max_size / 2.0f);
            AxisAlignedBox3d bounds = new AxisAlignedBox3d(centre - half, centre + half);

            // insert values into OctreeNodes
            Root = new OctreeNode { Bounds = bounds };
            for (int i = 0; i < mesh.VertexCount; i++) {
                Vector3d point = mesh.GetVertex(i);
                Vector3d normal = mesh.GetVertexNormal(i);
                Insert(point, normal);
            }

            Root.CalculateDivergencefield();
        }

        private void Insert(Vector3d point, Vector3d normal) {
            InsertRecursively(Root, point, normal, 0);
        }

        private void InsertRecursively(OctreeNode node, Vector3d point, Vector3d normal, int depth) {
            if (node.IsLeaf) {
                node.Points.Add(point);
                node.Normals.Add(normal);

                if (node.Points.Count > MaxPointsPerLeaf && depth < MaxDepth) {
                    node.Subdivide();
                    for (int i = 0; i < node.Points.Count; i++) {
                        InsertRecursively(node, node.Points[i], node.Normals[i], depth + 1);
                    }
                    node.Points.Clear();
                    node.Normals.Clear();
                }

                return;
            }

            for (int i = 0; i < 8; i++) {
                if (node.Children![i].Bounds.Contains(point)) {
                    InsertRecursively(node.Children[i], point, normal, depth + 1);
                    return;
                }
            }
        }
    }

    internal class OctreeNode {
        public AxisAlignedBox3d Bounds { get; init; }
        public OctreeNode[]? Children { get; set; }
        public bool IsLeaf => Children == null;

        // Sample Data
        public List<Vector3d> Points { get; set; } = [];
        public List<Vector3d> Normals { get; set; } = [];

        // Field Values
        public Vector3d NormalSum { get; set; } = Vector3d.Zero;
        public double Divergence { get; set; } = 0.0f;
        public double ScalerValue { get; set; } = 0.0f;

        public void CalculateDivergencefield() {
            //  no children
            if (IsLeaf) {
                // no normals to calculate
                if (Normals.Count == 0) {
                    Divergence = 0.0;
                    return;
                }

                // divergence = sum normal / leaf volume
                Vector3d sum = Vector3d.Zero;
                foreach(var n in Normals) { sum += n; }
                double volume = Bounds.Volume;

                if (volume > 0.0f) {
                    Divergence = sum.Dot(Bounds.Center.Normalized) / volume; 
                } else {
                    Divergence = 0.0f;
                }

                return;
            }

            foreach(var child in Children!) {
                child.CalculateDivergencefield();
            }
        }

        public void Subdivide() {
            Children = new OctreeNode[8]; // no longer a leaf
            Vector3d min = Bounds.Min;
            Vector3d max = Bounds.Max;
            Vector3d center = Bounds.Center;

            for (int i = 0; i < 8; i++) {
                Vector3d childMin = new Vector3d(
                    (i & 1) == 0 ? min.x : center.x,
                    (i & 2) == 0 ? min.y : center.y,
                    (i & 4) == 0 ? min.z : center.z
                );
                Vector3d childMax = new Vector3d(
                    (i & 1) == 0 ? center.x : max.x,
                    (i & 2) == 0 ? center.y : max.y,
                    (i & 4) == 0 ? center.z : max.z
                );
                Children[i] = new OctreeNode { Bounds = new AxisAlignedBox3d(childMin, childMax) };
            }
        }
    }
}
