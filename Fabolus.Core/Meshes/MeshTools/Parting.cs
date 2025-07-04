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

        int e0, e1, e2;
        Index2i tris, tri2;
        int tId;
        for(int i = 1; i < path.Length - 1; i++) {
            // reset
            tId = -1;

            // find triangles to the vertices
            e0 = mesh.FindEdge(path[i - 1], path[i]);

            // invalid edge
            if (e0< 0) {
                smoothPath.Add(path[i]);
                continue;
            }

            tris = mesh.GetEdgeT(e0);
            e1 = mesh.FindEdge(path[i], path[i + 1]);

            // invalid edge
            if (e1 < 0) {
                smoothPath.Add(path[i]);
                continue;
            }

            tri2 = mesh.GetEdgeT(e1);

            // get id of shared triangle
            if (tris.a == tri2.a || tris.a == tri2.b) { tId = tris.a; }
            if (tris.b == tri2.a || tris.b == tri2.b) { tId = tris.b; }

            // no triangle found, just add the vertex
            if (tId < 0) {
                smoothPath.Add(path[i]);
                continue;
            }
        }

        return smoothPath.ToArray();
    }
}
