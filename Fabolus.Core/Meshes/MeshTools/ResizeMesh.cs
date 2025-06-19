using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using g3;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools
{
    /// <summary>
    /// Reduce triangle count while maintiaining mesh shape
    /// </summary>
    /// <param name="mesh">the mesh to reduce</param>
    /// <param name="targetTriangleCount">the triangle count to attempt to reduce to</param>
    /// <returns>new mesh with reduced triangle count</returns>
    public static MeshModel Resize(MeshModel meshModel, int targetTriangleCount)
    {
        if (meshModel is null || meshModel.IsEmpty() || meshModel.Mesh.TriangleCount <= targetTriangleCount) { return meshModel; }

        // reduce mesh size
        DMesh3 mesh = Resize(meshModel.Mesh, targetTriangleCount);
        return new MeshModel(mesh);
    }

    /// <summary>
    /// Reduce triangle count while maintiaining mesh shape
    /// </summary>
    /// <param name="mesh">the mesh to reduce</param>
    /// <param name="targetTriangleCount">the triangle count to attempt to reduce to</param>
    /// <returns>new mesh with reduced triangle count</returns>
    public static Mesh Resize(Mesh meshModel, int targetTriangleCount)
    {
        if (meshModel is null || meshModel.ValidFaces.Count() == 0 || meshModel.ValidFaces.Count() <= targetTriangleCount)
        { return meshModel; }

        // reduce mesh size
        return Resize(meshModel.ToDMesh(), targetTriangleCount).ToMesh();
    }

    /// <summary>
    /// Reduce triangle count while maintiaining mesh shape
    /// </summary>
    /// <param name="mesh">the mesh to reduce</param>
    /// <param name="targetTriangleCount">the triangle count to attempt to reduce to</param>
    /// <returns>new mesh with reduced triangle count</returns>
    public static DMesh3 Resize(DMesh3 mesh, int targetTriangleCount)
    {
        if (mesh is null || mesh.IsEmpty() || mesh.TriangleCount <= targetTriangleCount) { return mesh; }

        // reduce mesh size
        DMeshAABBTree3 tree = new(new DMesh3(mesh), true);
        MeshProjectionTarget target = new()
        {
            Mesh = tree.Mesh,
            Spatial = tree,
        };

        Reducer reducer = new(mesh);
        reducer.SetProjectionTarget(target);
        reducer.ReduceToTriangleCount(targetTriangleCount);
        mesh.CompactInPlace(); //reorganize the triangles and verts

        return mesh;
    }
}
