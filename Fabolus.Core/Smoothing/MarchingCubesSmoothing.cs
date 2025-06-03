using Fabolus.Core.BolusModel;
using g3;
using gs;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float EdgeLength, float SmoothSpeed, int Iterations, int Cells);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        var edgeLength = settings.EdgeLength;
        var smoothSpeed = settings.SmoothSpeed;
        var iterations = settings.Iterations;
        var cells = settings.Cells;

        //offset mesh larger and then shrink a bit to lose details
        var mesh = OffsetBolus(bolus, 64, 0.5, 0.5);

        //Use the Remesher class to do a basic remeshing
        Remesher r = new Remesher(mesh);
        r.PreventNormalFlips = true;
        r.SetTargetEdgeLength(edgeLength);
        r.SmoothSpeedT = smoothSpeed;
        r.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));
        for (int k = 0; k < iterations; k++) { r.BasicRemeshPass(); }

        //marching cubes
        var num_cells = cells;
        DMesh3 smoothMesh = new();
        if (cells > 0) {
            double cell_size = mesh.CachedBounds.MaxDim / num_cells;
            MeshSignedDistanceGrid sdf = new MeshSignedDistanceGrid(mesh, cell_size);
            sdf.Compute();
            var iso = new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOrigin, sdf.CellSize);
            MarchingCubes c = new MarchingCubes();
            c.Implicit = iso;
            c.Bounds = mesh.CachedBounds;
            c.CubeSize = c.Bounds.MaxDim / cells;
            c.Bounds.Expand(3 * c.CubeSize);
            c.Generate();
            smoothMesh = c.Mesh;
        }

        if (smoothMesh is null) { throw new ArgumentNullException(nameof(smoothMesh)); }

        var newBolus = new Bolus(smoothMesh);
        newBolus.CopyOffsets(bolus);

        return newBolus;
    }

    private static DMesh3 OffsetBolus(Bolus bolus, int resolution, double offset1, double offset2) {
        BoundedImplicitFunction3d meshImplicit = meshToImplicitF(bolus.Mesh, resolution, offset1);
        var first =  generatMeshF(new ImplicitOffset3d() { A = meshImplicit, Offset = offset1 }, resolution);
        BoundedImplicitFunction3d secondMeshImplicit = meshToImplicitF(first, resolution, offset2);
        return generatMeshF(new ImplicitOffset3d() { A = secondMeshImplicit, Offset = offset2 }, resolution);
    }

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

        // cleanup
        MeshAutoRepair repair = new(c.Mesh);
        repair.Apply();

        return repair.Mesh;
    }
}
