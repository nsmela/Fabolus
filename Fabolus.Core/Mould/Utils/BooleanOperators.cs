using g3;
using static MR.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fabolus.Core.Meshes;

namespace Fabolus.Core.Mould.Utils;
public static class BooleanOperators {
    public static DMesh3 Union(DMesh3 mesh1, DMesh3 mesh2, int resolution = 128) {
        if (mesh1 is null || mesh1.TriangleCount <= 0) return mesh2;
        if (mesh2 is null || mesh2.TriangleCount <= 0) return mesh1;

        var task1 = Task.Run(() => meshToImplicitF(mesh1, resolution, 0.2f));
        var task2 = Task.Run(() => meshToImplicitF(mesh2, resolution, 0.2f));

        Task.WaitAll(task1, task2);

        //take the difference of the bolus mesh minus the tools
        var mesh = new ImplicitUnion3d() { A = task1.Result, B = task2.Result }; //{ A = meshA, B = meshB };

        //calculate the boolean mesh
        MarchingCubes c = new MarchingCubes();
        c.Implicit = mesh;
        c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;
        c.RootModeSteps = 5;
        c.Bounds = mesh.Bounds();
        c.CubeSize = c.Bounds.MaxDim / 96;
        c.Bounds.Expand(3 * c.CubeSize);
        c.Generate();
        g3.MeshNormals.QuickCompute(c.Mesh);

        return c.Mesh;
    }

    //public static DMesh3 Subtraction(DMesh3 mesh1, DMesh3 mesh2, int resolution = 128) {
    //    if (mesh1 is null || mesh1.TriangleCount <= 0) return mesh2;
    //    if (mesh2 is null || mesh2.TriangleCount <= 0) return mesh1;
    //
    //    var task1 = Task.Run(() => meshToImplicitF(mesh1, resolution, 0.2f));
    //    var task2 = Task.Run(() => meshToImplicitF(mesh2, resolution, 0.2f));
    //
    //    Task.WaitAll(task1, task2);
    //
    //    //take the difference of the bolus mesh minus the tools
    //    ImplicitDifference3d mesh = new ImplicitDifference3d() { A = task1.Result, B = task2.Result };
    //
    //    //calculate the boolean mesh
    //    MarchingCubes c = new MarchingCubes();
    //    c.Implicit = mesh;
    //    c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;
    //    c.RootModeSteps = 5;
    //    c.Bounds = mesh.Bounds();
    //    c.CubeSize = 1.0f; //c.Bounds.MaxDim / 64;
    //    c.Bounds.Expand(3 * c.CubeSize);
    //    c.Generate();
    //
    //    return c.Mesh;
    //}


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
        g3.MeshNormals.QuickCompute(c.Mesh);                           // generate normals
        return c.Mesh;   // write mesh
    }

    public static Result<DMesh3> Subtraction(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        try { 
            var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
            return Result<DMesh3>.Pass(result.mesh.ToDMesh());
        } catch(Exception e) {
            return Result<DMesh3>.Fail([ new MeshError(e.Message) ]);
        }

    }

    private static Mesh ToMesh(this DMesh3 mesh) {
        List<MR.DotNet.Vector3f> verts = mesh.Vertices().Select(v => new MR.DotNet.Vector3f((float)v.x, (float)v.y, (float)v.z)).ToList();
        List<ThreeVertIds> tris = mesh.Triangles().Select(t => new ThreeVertIds(t.a, t.b, t.c)).ToList();
        return Mesh.FromTriangles(verts, tris);
    }

    private static DMesh3 ToDMesh(this Mesh mesh) {
        DMesh3 result = new();

        foreach (var p in mesh.Points) {
            result.AppendVertex(new Vector3d(p.X, p.Y, p.Z));
        }
        foreach(var t in mesh.Triangulation) {
            result.AppendTriangle(t.v0.Id, t.v1.Id, t.v2.Id);
        }

        return result;
    }
}

