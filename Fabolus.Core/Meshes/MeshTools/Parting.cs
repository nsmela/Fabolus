using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {

    public static MeshModel PartingRegion(MeshModel model, double smoothingAngleInDegs = 5.0) {
        var angle_rads = Math.PI / 180.0 * smoothingAngleInDegs;
        var meshes = GetSmoothSurfaces(model.Mesh, angle_rads);

        MeshEditor editor = new(new DMesh3());
        for (int i = 2; i < meshes.Length; i++) {
            editor.AppendMesh(meshes[i]);
        }

        return new(editor.Mesh);
    }

    public static int[] PartingLine(MeshModel partingRegion, double[] start, double[] end) {
        DMesh3 mesh = new DMesh3(partingRegion.Mesh);
        
        int startId = MeshQueries.FindNearestVertex_LinearSearch(mesh, new Vector3d(start[0], start[1], start[2]));
        int endId = MeshQueries.FindNearestVertex_LinearSearch(mesh, new Vector3d(end[0], end[1], end[2]));

        DijkstraGraphDistance graph = DijkstraGraphDistance.MeshVertices(mesh);
        graph.TrackOrder = true;
        graph.AddSeed(endId, 0);
        graph.Compute();

        List<int> path = [];
        var success = graph.GetPathToSeed(startId, path);

        return graph.GetOrder().ToArray();
    }

    public static int[] PartingLineSmoothing(MeshModel model, int[] path) {
        DMesh3 mesh = model.Mesh; // reading, dont need to copy

        // step one: find any easy edge fixes and apply
        List<int> smoothPath = [];

        int eId0, eId1;
        Index4i e0, e1;
        int tId;
        double distance_squared;
        for(int i = 1; i < path.Length - 1; i++) {
            // reset
            tId = -1;

            // find triangles to the vertices
            eId0 = mesh.FindEdge(path[i - 1], path[i]);
            e0 = mesh.GetEdge(eId0);

            eId1 = mesh.FindEdge(path[i], path[i + 1]);
            e1 = mesh.GetEdge(eId1);

            // get id of shared triangle
            if (e0.c == e1.c || e0.c == e1.d) { tId = e0.c; }
            if (e0.d == e1.c || e0.d == e1.d) { tId = e0.d; }

            // no triangle found, add the current vertex
            if (tId < 0) {
                smoothPath.Add(path[i]);
                continue;
            }

            // new edge would be longer than the current edge, add current vertex
            distance_squared = mesh.GetVertex(path[i - 1]).DistanceSquared(mesh.GetVertex(path[i + 1]));
            if (e0.LengthSquared < distance_squared) {
                smoothPath.Add(path[i]);
                continue;
            }
        }

        // step two: find triangle pairs that could be skipped

        
        return smoothPath.ToArray();
    }
}
