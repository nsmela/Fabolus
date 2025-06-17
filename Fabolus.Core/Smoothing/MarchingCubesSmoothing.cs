using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System.Windows;
using static MR.DotNet;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float DeflateDistance, float InflateDistance, int Iterations, double CellSize);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        var deflateDistance = settings.DeflateDistance;
        var inflateDistance = settings.InflateDistance;
        var iterations = settings.Iterations;
        var cell_size = settings.CellSize;

        //shrink mesh to lose sharp details and inflate back to original size
        Mesh model = bolus.Mesh.Mesh.ToMesh();

        if (deflateDistance > 0) {
            for (int i = 0; i < iterations; i++) {
                model = MeshTools.OffsetDouble(model, deflateDistance);
            }
        }

        model = MeshTools.OffsetMesh(model, inflateDistance);
        return new Bolus(model.ToDMesh());

        // inflate
        DMesh3 mesh = MeshTools.OffsetMesh(model, inflateDistance).ToDMesh();

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
            g3.MeshNormals.QuickCompute(smoothMesh); // generate normals
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
