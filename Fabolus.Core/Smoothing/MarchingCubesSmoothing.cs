using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System.Windows;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float DeflateDistance, float InflateDistance, int Iterations, double CellSize);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        var deflateDistance = settings.DeflateDistance;
        var inflateDistance = settings.InflateDistance;
        var iterations = settings.Iterations;
        var cell_size = settings.CellSize;

        //shrink mesh to lose sharp details and inflate back to original size
        DMesh3 mesh = new(bolus.Mesh);

        if (deflateDistance > 0) {
            for (int i = 0; i < iterations; i++) {
                mesh = MeshTools.OffsetMesh(mesh, deflateDistance, cell_size);
                mesh = MeshTools.OffsetMesh(mesh, -deflateDistance, cell_size);
            }
        }

        // inflate
        mesh = MeshTools.OffsetMesh(mesh, inflateDistance, cell_size);

        // marching cubes
        DMesh3 smoothMesh = new();
        if (cell_size > 0) {
            MeshSignedDistanceGrid sdf = new(mesh, cell_size);
            sdf.Compute();

            DenseGridTrilinearImplicit iso = new(sdf.Grid, sdf.GridOrigin, sdf.CellSize);

            MarchingCubes cubes = new() {
                Implicit = iso,
                Bounds = mesh.CachedBounds,
                CubeSize = cell_size
            };

            cubes.Bounds.Expand(20 * cubes.CubeSize);
            cubes.Generate();
            smoothMesh = cubes.Mesh;
            MeshNormals.QuickCompute(smoothMesh); // generate normals
        }

        if (smoothMesh.IsEmpty()) { throw new ArgumentNullException(nameof(smoothMesh)); }
        
        // reduce mesh size
        DMeshAABBTree3 tree = new(new DMesh3(smoothMesh), true);
        MeshProjectionTarget target = new() {
            Mesh = tree.Mesh,
            Spatial = tree,
        };

        Reducer reducer = new(smoothMesh);
        reducer.SetProjectionTarget(target);
        int tri_count = bolus.Mesh.Mesh.TriangleCount * 2;
        reducer.ReduceToTriangleCount(tri_count);
        smoothMesh.CompactInPlace(); //reorganize the triangles and verts

        return new Bolus(smoothMesh);
    }

}
