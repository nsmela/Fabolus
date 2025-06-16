using g3;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static double[] SignedDistances(MeshModel origin, MeshModel target) {
        DMeshAABBTree3 tree = new(target.Mesh, true);

        List<double> result = [];
        foreach (Vector3d v in origin.Mesh.Vertices()) {
            int index = tree.FindNearestVertex(v);
            double distance = target.Mesh.GetVertex(index).Distance(v);

            if (tree.IsInside(v)) { distance *= -1; }// negative distance is inside the mesh

            result.Add(distance);
        }
        
        return result.ToArray();
    }
}
