using Fabolus.Core.Extensions;
using g3;
using NetTopologySuite.GeometriesGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PoissonRecon;
public static partial class PoissonReconstruction {
    // ref: https://hhoppe.com/poissonrecon.pdf

    private static Vector3d[] OFFSETS = [
        Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ,
        -Vector3d.AxisX, -Vector3d.AxisY, -Vector3d.AxisZ,
    ];

    private class Octree {
        public OctreeNode Root { get; set; }
        public int MaxPointsPerLeaf { get; set; } = 16;
        public int MaxDepth { get; set; } = 6;

        public double[]? DivergenceField { get; private set; } = null; // Divergence field for the octree
        public Dictionary<int, Dictionary<int, double>>? SparseMatrix { get; private set; } = null; // Sparse matrix representation of the octree
        public Dictionary<OctreeNode, int> NodeIndexes { get; private set; } = []; // Maps OctreeNode to its index in the sparse matrix

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

            // set up divergence field for each of the nodes within
            Root.CalculateDivergencefield();

            // index all leaf nodes
            List<OctreeNode> nodes = GetAllLeafNodes();
            NodeIndexes = [];
            for (int i = 0; i < nodes.Count; i++) {
                NodeIndexes[nodes[i]] = i;
            }

            // build sparse matrix and b vector
            // variables started outside loop to reduce memory garbage collection
            int n = nodes.Count;
            SparseMatrix = [];
            DivergenceField = new double[n];
            Func<OctreeNode, double> scale = (node) => 1.0 / node.Bounds.Volume;

            // this distance is used to reach the centre of a neighbouring OctreeNode by getting half the distance of the smallest possible cell
            // take max dimension and divide that by the number of cells at the maximum depth and divide by two
            double h = Root.Bounds.Width / Math.Pow(8.0, MaxDepth - 1) / 2.0;

            // offsets function to get the offsets for each node based on its bounds center and width
            // centre of node plus offset which is h plus current node's half width scaled with each offset direction
            Func<OctreeNode, Vector3d[]> offsets = (node) => OFFSETS.Select(o => node.Bounds.Center + o * (h + node.Bounds.Width / 2.0)).ToArray();

            Vector3d neighbourCentre = Vector3d.Zero;
            OctreeNode? neighbour_node = null;
            int node_index = -1;

            for (int i = 0; i < n; i++) {

                SparseMatrix[i] = [];
                SparseMatrix[i][i] = -6 * scale(nodes[i]);
                DivergenceField[i] = nodes[i].DivergenceWeighted; // volume-weighted divergence


                // Check 6 neighboring voxels
                h = nodes[i].Bounds.Width; // assuming cubic octree, so width is the same in all directions

                foreach (Vector3d offset in offsets(nodes[i])) {
                    //faster to calculate the neighbour node by checking if the point is within the bounds of the octree leaf
                    neighbour_node = Contains(nodes[i].Bounds.Center + offset);

                    if (neighbour_node is null) { continue; } // no neighbour

                    node_index = NodeIndexes[neighbour_node];
                    SparseMatrix[i][node_index] = scale(neighbour_node); // add neighbour contribution
                }
            }

        }

        public List<OctreeNode> GetAllLeafNodes() {
            List<OctreeNode> nodes = [];
            Traverse(Root);
            return nodes;

            void Traverse(OctreeNode node) {
                if (node.IsLeaf) {
                    nodes.Add(node);
                    return;
                }

                foreach (var child in node.Children!) {
                    Traverse(child);
                }
            }

        }

        private void Insert(Vector3d point, Vector3d normal) => InsertRecursively(Root, point, normal, 0);
        

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

        public OctreeNode? Contains(Vector3d point) => ContainsRecursively(Root, point);

        private OctreeNode? ContainsRecursively(OctreeNode node, Vector3d point) {
            if (node.IsLeaf) { return node; }

            foreach(OctreeNode child in node.Children!) {
                if (child.Bounds.Contains(point)) {
                    return ContainsRecursively(child, point);
                }
            }

            return null;
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
        public double DivergenceWeighted { get; set; } = 1.0f; // Divergence with volume weighting
        public double ScalerValue { get; set; } = 0.0f;

        public void CalculateDivergencefield() {
            //  no children
            if (IsLeaf) {
                // no normals to calculate
                if (Normals.Count == 0) {
                    Divergence = 0.0;
                    DivergenceWeighted = 0.0;
                    return;
                }

                // divergence = sum normal / leaf volume
                Vector3d sum = Vector3d.Zero;
                foreach (var n in Normals) { sum += n; }
                double volume = Bounds.Volume;

                if (volume > 0.0f) {
                    Divergence = sum.Dot(Bounds.Center.Normalized) / volume;
                } else {
                    Divergence = 0.0f;
                }

                DivergenceWeighted = Divergence * volume; // volume-weighted divergence

                return;
            }

            // has children
            foreach (var child in Children!) {
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
