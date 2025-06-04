using Fabolus.Core.BolusModel;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float DeflateDistance, float InflateDistance, int Iterations, int Cells);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        var deflateDistance = settings.DeflateDistance;
        var inflateDistance = settings.InflateDistance;
        var iterations = settings.Iterations;
        var cells = settings.Cells;

        //shrink mesh to lose details and inflate to get a smoother surface
        DMesh3 mesh = bolus.Mesh;
        for (int i = 0; i < iterations; i++) {
            mesh = MeshTools.OffsetMesh(mesh, -deflateDistance);
            mesh = MeshTools.OffsetMesh(mesh, inflateDistance);
        }

        //Use the Remesher class to do a basic remeshing
        //Remesher r = new Remesher(mesh);
        //r.PreventNormalFlips = true;
        //r.SetTargetEdgeLength(edgeLength);
        //r.SmoothSpeedT = smoothSpeed;
        //r.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));
        //for (int k = 0; k < iterations; k++) { r.BasicRemeshPass(); }

        //marching cubes
        DMesh3 smoothMesh = new();
        if (cells > 0) {
            double cell_size = mesh.CachedBounds.MaxDim / cells;
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

        DMeshAABBTree3 spatial = new(bolus.Mesh.Mesh, true);
        MeshICP icp = new(smoothMesh, spatial);
        icp.Solve();
        icp.UpdateVertices(smoothMesh);

        var newBolus = new Bolus(smoothMesh);
        newBolus.CopyOffsets(bolus);

        return newBolus;
    }

}
