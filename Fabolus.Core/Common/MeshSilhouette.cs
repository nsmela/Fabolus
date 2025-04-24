using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace Fabolus.Core.Common;
public class MeshSilhouette {
    private DMesh3 Mesh {get; init; }
    private Dictionary<int, bool> TriFacingCache { get; set; } = [];

    public List<PolyLine3d> Loops { get; private set; } = [];
    public List<Segment3d> Edges { get; private set; } = [];

    public PolyLine3d MainLoop => Loops.OrderByDescending(x => x.Count()).First();

    /// <summary>
    /// Constructor for the silhouette finder.
    /// </summary>
    /// <param name="mesh">The input 3D mesh.</param>
    public MeshSilhouette(DMesh3 mesh) {
        //validation checks
        if (mesh is null) { throw new ArgumentNullException(nameof(mesh)); }
        if (!mesh.IsCompact) { Console.WriteLine("Warning: MeshSilhouette works best with compact meshes."); }
        if (mesh.TriangleCount == 0) { throw new ArgumentException("Mesh must have triangles.", nameof(mesh)); }
        // Normals needed for robust face orientation check
        if (!mesh.HasVertexNormals) { MeshNormals.QuickCompute(mesh); }// Compute if missing

        Mesh = mesh;
    }

    /// <summary>
    /// Computes the silhouette based on the view direction.
    /// </summary>
    /// <param name="viewDirection">The direction the viewer is looking FROM.</param>
    public void Compute(Vector3d viewDirection) {
        viewDirection.Normalize();

        Loops = new();
        Edges = new();
        TriFacingCache = new();

        //stores silhouette edges
        //Key: vertex index, Value: List of vertex indices connected by a silhouette edge
        Dictionary<int, List<int>> graph = new();

        foreach(var edgeId in Mesh.EdgeIndices()) {
            var index = Mesh.GetEdgeT(edgeId);
            int a = index.a;
            int b = index.b;

            // Boundary edges are always part of the silhouette if only one triangle exists
            if (b == DMesh3.InvalidID) {
                // Check orientation of the single triangle
                if (IsTriangleFacing(a, viewDirection)) { AddSilhouetteEdge(edgeId, graph); }

                continue; // Move to the next edge
            }

            // For internal edges, check if one triangle faces the view and the other faces away
            bool facingA = IsTriangleFacing(a, viewDirection);
            bool facingB = IsTriangleFacing(b, viewDirection);

            // One faces towards, one faces away -> silhouette edge
            if (facingA != facingB) { AddSilhouetteEdge(edgeId, graph); }
        }

        BuildLoops(graph);
    }

    /// <summary>
    /// Helper to check if a triangle is facing the view direction.
    /// Caches results for efficiency.
    /// </summary>
    private bool IsTriangleFacing(int triangleId, Vector3d viewDirection) {
        if (TriFacingCache.TryGetValue(triangleId, out bool isFacing)) {
            return isFacing;
        }

        Vector3d normal = Mesh.GetTriNormal(triangleId);
        // Dot product > 0 means the normal points generally towards the view direction (back-facing)
        // Dot product < 0 means the normal points generally away from the view direction (front-facing)
        // We consider triangles "facing" if their normal points away from the view direction (dot < 0).
        // Adjust the sign depending on your convention (is viewDirection where camera is, or where it looks?)
        // Here, viewDirection is where the camera *is* (e.g., +Z), looking towards origin.
        // A normal pointing towards +Z (dot > 0) is back-facing.
        // A normal pointing towards -Z (dot < 0) is front-facing.
        // Silhouette requires one front, one back. Let's define "facing" as "front-facing".
        isFacing = normal.Dot(viewDirection) < -MathUtil.ZeroTolerance; // Triangle normal points away from view vector

        TriFacingCache[triangleId] = isFacing;
        return isFacing;
    }

    /// <summary>
    /// Adds an edge's vertices to the silhouette graph and the raw Edges list.
    /// </summary>
    private void AddSilhouetteEdge(int edgeId, Dictionary<int, List<int>> graph) {
        Index2i edgeVerts = Mesh.GetEdgeV(edgeId);
        int vA = edgeVerts.a;
        int vB = edgeVerts.b;

        // Add to graph (adjacency list)
        if (!graph.ContainsKey(vA)) { graph[vA] = new List<int>(); }
        if (!graph.ContainsKey(vB)) { graph[vB] = new List<int>(); }
        graph[vA].Add(vB);
        graph[vB].Add(vA);

        // Add to raw edge list
        Edges.Add(new Segment3d(Mesh.GetVertex(vA), Mesh.GetVertex(vB)));
    }

    /// <summary>
    /// Builds closed PolyLine3d loops from the silhouette edge graph.
    /// Uses a simple traversal strategy. Might not handle all complex cases perfectly.
    /// </summary>
    private void BuildLoops(Dictionary<int, List<int>> graph) {
        HashSet<int> visitedVertices = new HashSet<int>();

        foreach (int startVertexId in graph.Keys) {
            if (visitedVertices.Contains(startVertexId)) { continue; }

            // Start tracing a new loop
            List<Vector3d> currentLoopVertices = new List<Vector3d>();
            int currentVertexId = startVertexId;
            int previousVertexId = -1; // To avoid immediately going back

            while (currentVertexId != -1 && !visitedVertices.Contains(currentVertexId)) {
                visitedVertices.Add(currentVertexId);
                currentLoopVertices.Add(Mesh.GetVertex(currentVertexId));

                List<int> neighbors = graph[currentVertexId];
                int nextVertexId = -1;

                // Find the next unvisited neighbor that isn't the one we just came from
                foreach (int neighborId in neighbors) {
                    if (neighborId != previousVertexId) // Don't go back immediately
                    {
                        // If we found the start vertex again and the loop has > 2 points, close it
                        if (neighborId == startVertexId && currentLoopVertices.Count > 2) {
                            nextVertexId = startVertexId; // Signal loop closure
                            break;
                        }
                        // Otherwise, pick the first valid neighbor to continue path
                        if (!visitedVertices.Contains(neighborId)) {
                            nextVertexId = neighborId;
                            break; // Take the first valid path continuation
                        }
                    }
                }

                // If we are closing the loop back to the start
                if (nextVertexId == startVertexId) {
                    // Loop closed successfully
                    previousVertexId = currentVertexId; // Update previous
                    currentVertexId = -1; // Stop loop tracing
                }

                // If we found a valid next step
                else if (nextVertexId != -1) {
                    previousVertexId = currentVertexId;
                    currentVertexId = nextVertexId;
                }

                // If we hit a dead end or an already visited vertex unexpectedly (shouldn't happen in clean loops)
                else {
                    // Check if the last neighbor is the start vertex (handles 2-edge cases or graph issues)
                    if (neighbors.Count > 0 && neighbors.LastOrDefault(n => n != previousVertexId) == startVertexId && currentLoopVertices.Count > 1) {
                        // Attempt to close loop
                        previousVertexId = currentVertexId;
                        currentVertexId = -1; // Stop loop tracing
                    } else {
                        Console.WriteLine($"Warning: Loop tracing stopped unexpectedly at vertex {currentVertexId}. Loop might be incomplete.");
                        currentVertexId = -1; // Stop tracing this path
                    }
                }
            } // end while tracing loop


            // If a valid loop was formed (at least 3 vertices)
            if (currentLoopVertices.Count > 2) {
                // Check if the loop is geometrically closed (last point near first point)
                // This might be needed if the graph traversal logic doesn't explicitly add the start point at the end.
                if (currentLoopVertices[0].DistanceSquared(currentLoopVertices[currentLoopVertices.Count - 1]) > 1e-8) {
                    // Optional: Add the starting vertex to explicitly close the polyline if needed by PolyLine3d constructor or usage
                    // currentLoopVertices.Add(currentLoopVertices[0]);
                }

                Loops.Add(new PolyLine3d(currentLoopVertices.ToArray()));
            } else if (currentLoopVertices.Count > 0) {
                Console.WriteLine($"Warning: Found silhouette segment(s) starting at {startVertexId} that did not form a closed loop of sufficient length.");
            }

        } // end foreach start vertex
    }
}
