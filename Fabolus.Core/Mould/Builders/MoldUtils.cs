using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Builders;
public static class MoldUtils {
    public static DMesh3 OffsetMeshD(DMesh3 mesh, double offset, int resolution = 64) {
        BoundedImplicitFunction3d meshImplicit = meshToImplicitF(mesh, resolution, offset);
        return generatMeshF(new ImplicitOffset3d() { A = meshImplicit, Offset = offset }, resolution);
    }

    public static DMesh3 OffsetMesh(DMesh3 mesh, double offset, int resolution = 64) =>  OffsetMeshD(mesh, offset, resolution);


    // meshToImplicitF() generates a narrow-band distance-field and
    // returns it as an implicit surface, that can be combined with other implicits                       
    private static Func<DMesh3, int, double, BoundedImplicitFunction3d> meshToImplicitF = (meshIn, numcells, max_offset) => {
        double meshCellsize = meshIn.CachedBounds.MaxDim / numcells;
        MeshSignedDistanceGrid levelSet = new MeshSignedDistanceGrid(meshIn, meshCellsize);
        levelSet.ExactBandWidth = (int)(max_offset / meshCellsize) + 1;
        levelSet.Compute();
        return new DenseGridTrilinearImplicit(levelSet.Grid, levelSet.GridOrigin, levelSet.CellSize);
    };

    // generateMeshF() meshes the input implicit function at
    // the given cell resolution, and writes out the resulting mesh    
    private static DMesh3 generatMeshF(BoundedImplicitFunction3d root, int numcells) {
        MarchingCubes c = new MarchingCubes();
        c.Implicit = root;
        c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;      // cube-edge convergence method
        c.RootModeSteps = 5;                                        // number of iterations
        c.Bounds = root.Bounds();
        c.CubeSize = c.Bounds.MaxDim / numcells;
        c.Bounds.Expand(3 * c.CubeSize);                            // leave a buffer of cells
        c.Generate();
        MeshNormals.QuickCompute(c.Mesh);                           // generate normals
        return c.Mesh;   // write mesh
    }
}
