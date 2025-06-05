using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System.Windows;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float DeflateDistance, float InflateDistance, int Iterations, int Cells);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        var deflateDistance = settings.DeflateDistance;
        var inflateDistance = settings.InflateDistance;
        var iterations = settings.Iterations;
        var cells = settings.Cells;

        //shrink mesh to lose sharp details and inflate back to original size
        DMesh3 mesh = new(bolus.Mesh);

        if (deflateDistance > 0) {
            for (int i = 0; i < iterations; i++) {
                mesh = MeshTools.OffsetMesh(mesh, -deflateDistance, cells);
                mesh = MeshTools.OffsetMesh(mesh, deflateDistance, cells);
            }
        }

        // inflate
        mesh = MeshTools.OffsetMesh(mesh, inflateDistance);

        // marching cubes
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
            MeshNormals.QuickCompute(smoothMesh); // generate normals
        }

        if (smoothMesh.IsEmpty()) { throw new ArgumentNullException(nameof(smoothMesh)); }

        // reposition the smoothed mesh to the original mesh
        //DMeshAABBTree3 spatial = new(bolus.Mesh.Mesh, true);
        //spatial.Build();
        //MeshICP icp = new(smoothMesh, spatial);
        //icp.Solve(true);
        //icp.UpdateVertices(smoothMesh);
        
        // reduce mesh size
        //Reducer reducer = new(smoothMesh);
        //DMeshAABBTree3 tree = new(new DMesh3(smoothMesh), true);
        //MeshProjectionTarget target = new(tree.Mesh, tree);
        //reducer.SetProjectionTarget(target);
        //reducer.ReduceToTriangleCount(20000);

        //MeshAutoRepair repair = new(smoothMesh);
        //var pass = repair.Apply();

        var newBolus = new Bolus(smoothMesh);
        newBolus.CopyOffsets(bolus);

        return newBolus;
    }

}
