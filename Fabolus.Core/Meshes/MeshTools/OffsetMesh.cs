using g3;
using gs;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static DMesh3 OffsetMesh(DMesh3 mesh, double offset, double cell_size) {
        BoundedImplicitFunction3d meshImplicit = meshToImplicitF(mesh, cell_size, offset);
        return generatMeshF(new ImplicitOffset3d() { A = meshImplicit, Offset = offset }, cell_size);
    }

    // meshToImplicitF() generates a narrow-band distance-field and
    // returns it as an implicit surface, that can be combined with other implicits                       
    private static Func<DMesh3, double, double, BoundedImplicitFunction3d> meshToImplicitF = (meshIn, cell_size, max_offset) => {
        //double meshCellsize = meshIn.CachedBounds.MaxDim / numcells;
        MeshSignedDistanceGrid levelSet = new MeshSignedDistanceGrid(meshIn, cell_size);
        levelSet.ExactBandWidth = (int)(max_offset / cell_size) + 25;
        levelSet.Compute();
        return new DenseGridTrilinearImplicit(levelSet.Grid, levelSet.GridOrigin, levelSet.CellSize);
    };

    // generateMeshF() meshes the input implicit function at
    // the given cell resolution, and writes out the resulting mesh    
    private static DMesh3 generatMeshF(BoundedImplicitFunction3d root, double cell_size) {
        MarchingCubes c = new MarchingCubes();
        c.Implicit = root;
        c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;      // cube-edge convergence method
        c.RootModeSteps = 5;                                        // number of iterations
        c.Bounds = root.Bounds();
        c.CubeSize = cell_size;
        c.Bounds.Expand(3 * c.CubeSize);                            // leave a buffer of cells
        c.Generate();
        MeshNormals.QuickCompute(c.Mesh);                           // generate normals

        // cleanup
        MeshAutoRepair repair = new(c.Mesh);
        repair.Apply();

        return repair.Mesh;
    }

}
