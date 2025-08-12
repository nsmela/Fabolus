using Fabolus.Core.Extensions;
using g3;
using gs;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    internal static DMesh3 OffsetMesh(DMesh3 mesh, double offset, double cell_size) {
        BoundedImplicitFunction3d meshImplicit = meshToImplicitF(mesh, cell_size, offset);
        return generatMeshF(new ImplicitOffset3d() { A = meshImplicit, Offset = offset }, cell_size);
    }

    // meshToImplicitF() generates a narrow-band distance-field and
    // returns it as an implicit surface, that can be combined with other implicits                       
    internal static Func<DMesh3, double, double, BoundedImplicitFunction3d> meshToImplicitF = (meshIn, cell_size, max_offset) => {
        MeshSignedDistanceGrid levelSet = new MeshSignedDistanceGrid(meshIn, cell_size);
        levelSet.ExactBandWidth = (int)(max_offset / cell_size) + 25;
        levelSet.Compute();
        return new DenseGridTrilinearImplicit(levelSet.Grid, levelSet.GridOrigin, levelSet.CellSize);
    };

    // generateMeshF() meshes the input implicit function at
    // the given cell resolution, and writes out the resulting mesh    
    private static DMesh3 generatMeshF(BoundedImplicitFunction3d root, double cell_size) {
        MarchingCubes c = new MarchingCubes() {
            Implicit = root,
            RootMode = MarchingCubes.RootfindingModes.LerpSteps,    // cube-edge convergence method
            RootModeSteps = 4,                                      // number of iterations
            Bounds = root.Bounds(),
            CubeSize = cell_size,
        };

        c.Bounds.Expand(3 * c.CubeSize);    // leave a buffer of cells
        c.Generate();

        g3.MeshNormals.QuickCompute(c.Mesh);   // generate normals

        // cleanup
        MeshAutoRepair repair = new(c.Mesh);
        repair.Apply();

        return repair.Mesh;
    }


    internal static Mesh OffsetMesh(Mesh mesh, float offsetDistance, float cellSize = 0.0f) {
        MeshPart mp = new(mesh);

        OffsetParameters parms = new() {
            voxelSize = cellSize > 0 ? cellSize : Offset.SuggestVoxelSize(mp, 1e6f),
        };

        var result = Offset.OffsetMesh(mp, offsetDistance, parms);

        return result;
    }

    internal static Mesh OffsetDouble(Mesh mesh, float offsetDistance) {
        MeshPart mp = new(mesh);
        OffsetParameters parms = new() {
            voxelSize = Offset.SuggestVoxelSize(mp, 1e6f),
        };

        return Offset.DoubleOffsetMesh(mp, offsetDistance, -offsetDistance, parms);
    }

    internal static Mesh OffsetDouble(Mesh mesh, float offsetDistance, float voxelSize) {
        MeshPart mp = new(mesh);
        OffsetParameters parms = new() {
            voxelSize = voxelSize,
        };
        return Offset.DoubleOffsetMesh(mp, offsetDistance, -offsetDistance, parms);
    }

    public static MeshModel OffsetModel(MeshModel model, double distance) {
        Mesh mesh = model.Mesh.ToMesh();
        var offset = OffsetMesh(mesh, (float)distance);
        return new MeshModel(offset);
    }
}
