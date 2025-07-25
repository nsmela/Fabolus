using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;

public class Graph {

    private class PathNode {
        public int VertexId;
        public double GCost;
        public double HCost;
        public double FCost => GCost + HCost;
        public PathNode Parent;
        public Vector3d Direction;

        public PathNode(int vert_id, double gcost = double.MaxValue) {
            VertexId = vert_id;
            GCost = gcost;
        }
    }

    private readonly DMesh3 _mesh;

    public Graph(DMesh3 mesh) {
        _mesh = mesh;
    }


    public int[] FindPath(int start_index, int end_index, bool exclude_start_and_finish = false) {

        PathNode start_node = new(start_index, 0.0) {
            HCost = GetVertexDistance(start_index, end_index),
        };

        var nodes = new Dictionary<int, PathNode> {
            { start_index, start_node }
        };

        List<PathNode> open_set = new() { start_node };
        HashSet<int> closed_set = new() { };

        // initialize variables for loop
        PathNode current_node;
        Vector3d current_v;
        double new_cost;
        Vector3d direction = Vector3d.Zero;
        while (open_set.Count > 0) {
            // get node with lowest FCost
            open_set.Sort((a, b) => a.FCost.CompareTo(b.FCost));
            current_node = open_set[0];

            // check if this is the goal
            if (current_node.VertexId == end_index) {
                return RetracePath(current_node, exclude_start_and_finish);
            }

            // ensure this node isn't processed again
            open_set.Remove(current_node);
            closed_set.Add(current_node.VertexId);

            // get direction to current node for reference
            current_v = _mesh.GetVertex(current_node.VertexId);
            direction = current_v - direction;

            // evaluate neighbours
            foreach (int id in _mesh.VtxVerticesItr(current_node.VertexId)) {
                if (closed_set.Contains(id)) { continue; } //already processed

                new_cost = GetVertexDistance(id, current_node.VertexId);
                new_cost *= AnglePenalty(_mesh.GetVertexNormal(id), Vector3d.AxisY);
                new_cost += current_node.GCost;

                // if node isn't tracked, add it
                if (!nodes.TryGetValue(id, out PathNode neighbour_node)) {
                    neighbour_node = new PathNode(id);
                    nodes[id] = neighbour_node;
                }

                // evaluate the node
                // if it's closer to the current node than from before, or a new node
                if (new_cost < neighbour_node.GCost) {
                    neighbour_node.GCost = new_cost;
                    neighbour_node.HCost = GetVertexDistance(neighbour_node.VertexId, end_index);
                    neighbour_node.Parent = current_node;
                }

                if (!open_set.Contains(neighbour_node)) {
                    open_set.Add(neighbour_node);
                }
            }
        }

        return []; // no path found
    }

    private int[] RetracePath(PathNode node, bool exclude_start_and_end) {
        List<int> path = [];
        PathNode current_node = node;

        while (current_node != null) {
            path.Add(current_node.VertexId);
            current_node = current_node.Parent;
        }

        if (exclude_start_and_end) {
            path.RemoveAt(path.Count - 1); // remove the end node
            path.RemoveAt(0); // remove the start node
        }

        path.Reverse();
        return path.ToArray();
    }

    private double GetVertexDistance(int v0, int v1) =>
        _mesh.GetVertex(v0).Distance(_mesh.GetVertex(v1));

    private double AnglePenalty(Vector3d v0, Vector3d reference) =>
        1 + 2 * Math.Abs((MathUtil.HalfPI - v0.AngleR(reference)) / MathUtil.HalfPI);

}
