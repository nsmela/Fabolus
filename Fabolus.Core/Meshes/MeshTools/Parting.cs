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

    public static double[][] PartingLine(MeshModel partingRegion, double[] start, double[] end) {
        DMesh3 mesh = new DMesh3(partingRegion.Mesh);
        
        int startId = MeshQueries.FindNearestVertex_LinearSearch(mesh, new Vector3d(start[0], start[1], start[2]));
        int endId = MeshQueries.FindNearestVertex_LinearSearch(mesh, new Vector3d(end[0], end[1], end[2]));

        DijkstraGraphDistance graph = DijkstraGraphDistance.MeshVertices(mesh);
        graph.TrackOrder = true;
        graph.AddSeed(endId, 0);
        graph.Compute();

        List<int> path = [];
        var success = graph.GetPathToSeed(startId, path);

        var points = graph.GetOrder().Select(i => mesh.GetVertex(i).ToDoubles());

        return points.ToArray();
    }

    private static double[] ToDoubles(this Vector3d vector) => new double[] { vector.x, vector.y, vector.z };
    
}
