using Fabolus.Core.BolusModel;
using g3;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float EdgeLength, float SmoothSpeed, int Iterations, int Cells);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        var edgeLength = settings.EdgeLength;
        var smoothSpeed = settings.SmoothSpeed;
        var iterations = settings.Iterations;
        var cells = settings.Cells;

        //Use the Remesher class to do a basic remeshing
        DMesh3 mesh = new DMesh3(bolus.Mesh);
        //Remesher r = new Remesher(mesh);
       // r.PreventNormalFlips = true;
        //r.SetTargetEdgeLength(edgeLength);
       // r.SmoothSpeedT = smoothSpeed;
       // r.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));
        //for (int k = 0; k < iterations; k++) { r.BasicRemeshPass(); }

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
}
