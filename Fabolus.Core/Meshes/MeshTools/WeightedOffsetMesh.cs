using Fabolus.Core.Extensions;
using g3;
using gs;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static MeshModel WeightedOffsetMesh(MeshModel model, float[] weights, float offsetDistance, float cellSize = 1.5f) {
        DMesh3 mesh = new DMesh3(model.Mesh);

        BoundedImplicitFunction3d meshImplicit = weightedMeshToImplicitF(mesh, cellSize, offsetDistance, weights);
        mesh = generatMeshF(new ImplicitOffset3d() { A = meshImplicit, Offset = offsetDistance }, cellSize);
        return new MeshModel(mesh);
    }

    private static Func<DMesh3, double, double, float[], BoundedImplicitFunction3d> weightedMeshToImplicitF = (meshIn, cell_size, max_offset, weights) => {
        MeshOffsetSignedDistanceGrid levelSet = new (meshIn, cell_size, weights);
        levelSet.ExactBandWidth = (int)(max_offset / cell_size) + 25;
        levelSet.Compute();
        return new DenseGridTrilinearImplicit(levelSet.Grid, levelSet.GridOrigin, levelSet.CellSize);
    };

}
