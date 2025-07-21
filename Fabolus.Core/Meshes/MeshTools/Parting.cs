using Clipper2Lib;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.PartingTools;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using static g3.DMesh3;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {
    // ref: https://www.gradientspace.com/tutorials/dmesh3

    public static MeshModel PartingRegion(MeshModel model, double smoothingAngleInDegs = 5.0) {
        var angle_rads = Math.PI / 180.0 * smoothingAngleInDegs;
        var meshes = GetSmoothSurfaces(model.Mesh, angle_rads);

        MeshEditor editor = new(new DMesh3());
        for (int i = 2; i < meshes.Length; i++) {
            editor.AppendMesh(meshes[i]);
        }

        return new(editor.Mesh);
    }

    public static int[] PartingLine(MeshModel partingRegion, double[] start, double[] end) {
        DMesh3 mesh = new DMesh3(partingRegion.Mesh);

        int startId = MeshQueries.FindNearestVertex_LinearSearch(mesh, new Vector3d(start[0], start[1], start[2]));
        int endId = MeshQueries.FindNearestVertex_LinearSearch(mesh, new Vector3d(end[0], end[1], end[2]));

        DijkstraGraphDistance graph = DijkstraGraphDistance.MeshVertices(mesh);
        graph.TrackOrder = true;
        graph.AddSeed(endId, 0);
        graph.Compute();

        List<int> path = [];
        var success = graph.GetPathToSeed(startId, path);
        var result = graph.GetOrder().ToArray();

        result = RemoveSingleTriangles(mesh, result);
        return result;
    }

    public static int[] PartingLineSmoothing(DMesh3 mesh, int[] path, int iterations = 4) {
        // step one: find any easy edge fixes and apply
        List<int> smoothPath = [];

        int eId0, eId1;
        Index4i e0, e1;
        int tId;
        double distance_squared;
        for (int i = 1; i < path.Length - 1; i++) {
            // reset
            tId = -1;

            // find triangles to the vertices
            eId0 = mesh.FindEdge(path[i - 1], path[i]);
            if (eId0 < 0) {
                // no edge found, add the current vertex
                smoothPath.Add(path[i]);
                continue;
            }

            e0 = mesh.GetEdge(eId0);

            eId1 = mesh.FindEdge(path[i], path[i + 1]);
            if (eId1 < 0) {
                // no edge found, add the current vertex
                smoothPath.Add(path[i]);
                continue;
            }

            e1 = mesh.GetEdge(eId1);

            // get id of shared triangle
            if (e0.c == e1.c || e0.c == e1.d) { tId = e0.c; }
            if (e0.d == e1.c || e0.d == e1.d) { tId = e0.d; }

            // no triangle found, add the current vertex
            if (tId < 0) {
                smoothPath.Add(path[i]);
                continue;
            }

            // new edge would be longer than the current edge, add current vertex
            distance_squared = mesh.GetVertex(path[i - 1]).DistanceSquared(mesh.GetVertex(path[i + 1]));
            if (e0.LengthSquared < distance_squared) {
                smoothPath.Add(path[i]);
                continue;
            }
        }

        // step two: find triangle pairs that could be skipped
        List<int> edges = [];
        for (int i = 1; i < smoothPath.Count; i++) {
            var edge = mesh.FindEdge(smoothPath[i - 1], smoothPath[i]);
            if (edge < 0) {
                continue;
                //throw new Exception($"Edge not found between vertices {smoothPath[i - 1]} and {smoothPath[i]}");
            }
            edges.Add(edge);
        }

        g3.EdgeLoop loop = new(mesh, smoothPath.ToArray(), edges.ToArray(), true);
        MeshLoopSmooth smoother = new(mesh, loop);
        smoother.Alpha = 0.8;
        for (int i = 0; i < iterations; i++) { smoother.Smooth(); }

        return smoothPath.ToArray();
    }

    public static int[] RemoveSingleTriangles(DMesh3 mesh, int[] path, int iterations = 4) {
        List<int> smoothPath = [];

        int v0, v1, v2;
        int eId0, eId1;
        Index4i e0, e1;
        int tId;
        double distance_squared;
        for (int i = 1; i < path.Length - 1; i++) {
            // reset
            tId = -1;

            v0 = path[i - 1];
            v1 = path[i];
            v2 = path[i + 1];

            // find triangles to the vertices
            eId0 = mesh.FindEdge(v0, v1);
            if (eId0 < 0) {
                // no edge found, skip
                continue;
            }

            e0 = mesh.GetEdge(eId0);

            eId1 = mesh.FindEdge(v1, v2);
            if (eId1 < 0) {
                // no edge found, skip
                continue;
            }

            e1 = mesh.GetEdge(eId1);

            // get id of shared triangle
            if (e0.c == e1.c || e0.c == e1.d) { tId = e0.c; }
            if (e0.d == e1.c || e0.d == e1.d) { tId = e0.d; }

            // no triangle found, add the current vertex
            if (tId < 0) {
                smoothPath.Add(path[i]);
                continue;
            }

            // new edge would be longer than the current edge, add current vertex
            distance_squared = mesh.GetVertex(v0).DistanceSquared(mesh.GetVertex(v1));
            if (e0.LengthSquared + e1.LengthSquared < distance_squared) {
                smoothPath.Add(path[i]);
                continue;
            }
        }

        return smoothPath.ToArray();
    }

    public static int[] DijkstraSmoothing(DMesh3 mesh, int[] path, int iterations = 4) {
        DMesh3 graph_mesh = new(mesh);

        DijkstraGraphDistance graph = DijkstraGraphDistance.MeshVertices(graph_mesh);
        graph.TrackOrder = true;
        graph.AddSeed(path[0], 0);

        int increment = path.Length / 8;
        for (int i = increment; i < path.Length; i+=increment) {
            graph.AddSeed(path[i], 0);
        }
        graph.Compute();

        return graph.GetOrder().ToArray();
    }

    public static int[] GraphSmoothing(DMesh3 mesh, int[] path) {
        Graph graph = new(mesh);
        List<int> result = [];

        int increment = 12;
        int last_id = path[0];
        for (int i = increment; i < path.Length; i += increment) {
            result.AddRange(graph.FindPath(last_id, path[i]));
            last_id = path[i];
        }

        return result.ToArray();
    }

    public static Result<MeshModel> GeneratePartingMesh(MeshModel model, int[] path_verts, double[] pull_direction, double extrusion_length = 5.0) {
        DMesh3 mesh = new DMesh3(model.Mesh);
        Vector3d direction = new Vector3d(pull_direction[0], pull_direction[1], pull_direction[2]).Normalized;

        // project path verts into a 2d polygon on the plane based on the pull direction
        Polygon2d polygon = new Polygon2d(path_verts.Select(vId => new Vector2d(mesh.GetVertex(vId).x, mesh.GetVertex(vId).z)));
        
        // convert into Clipper2 path
        PathsD paths = new() { new PathD(polygon.Vertices.Select(v => new PointD(v.x, v.y)).ToList()) };
        PathsD solution = Clipper.InflatePaths(paths, -extrusion_length, JoinType.Round, EndType.Polygon);

        if (solution is null || solution.Count == 0) { return new MeshError("Failed to generate parting mesh: No solution found."); }

        // need to prep for stiching the two loops together
        // stiching requires both loops (inner and outer) to be the same length
        // this is done by instead creating a list of xz vectors to add to the original line

        List<Vector3d> offsets = [];
        DMesh3 vert_mesh = new();   // Create a new mesh for the parting surface
        List<int> inner_loop = [];
        List<int> outer_loop = [];
        Vector3d vert;
        Vector3d offset;

        for(int i = 0; i < path_verts.Length; i++) {
            // set inner loop
            vert = mesh.GetVertex(path_verts[i]);
            inner_loop.Add(vert_mesh.AppendVertex(vert));

            // find the closest point in the solution
            PointD point = paths[0][i];
            PointD closest = solution[0].MinBy(x => x.DistanceSquared(point));
            offset = new Vector3d(point.x - closest.x, 0, point.y - closest.y);

            // set outer loop
            outer_loop.Add(vert_mesh.AppendVertex(vert + offset));
        }

        MeshEditor editor = new(vert_mesh);
        editor.StitchLoop(inner_loop.ToArray(), outer_loop.ToArray()); // stitch the inner and outer loops together

        DMesh3 result = editor.Mesh;

        // extrude outer region backwards
        List<int> extruded_loop = [];
        foreach (int vId in outer_loop.ToArray()) {
            extruded_loop.Add(result.AppendVertex(result.GetVertex(vId) + direction * 100));
        }

        editor = new(result);
        editor.StitchLoop(outer_loop.ToArray(), extruded_loop.ToArray());

        MeshBoundaryLoops loops = new(editor.Mesh);

        SimpleHoleFiller filler = new(editor.Mesh, loops[1]);
        filler.Fill();

        return new MeshModel(filler.Mesh);
    }

    public static Result<MeshModel> JoinMeshes(DMesh3 parting, DMesh3 face) {
        MeshEditor editor = new(parting);
        DMesh3 new_face = new(face);
        new_face.ReverseOrientation();
        editor.AppendMesh(new_face);

        MeshAutoRepair repair = new(editor.Mesh);
        repair.Apply();

        DMesh3 result = repair.Mesh;

        // make solid and manifold
        MeshSignedDistanceGrid sdf = new(result, 3.0);
        sdf.Compute();

        DenseGridTrilinearImplicit grid = new(sdf.Grid, sdf.GridOrigin, sdf.CellSize);

        MarchingCubes cubes = new() {
            Implicit = grid,
            Bounds = result.CachedBounds,
            CubeSize = grid.CellSize,
        };

        cubes.Bounds.Expand( 3 * cubes.CubeSize );
        cubes.Generate();

        return new MeshModel(cubes.Mesh);

    } 

    public static MeshModel[] FinalPass(MeshModel tool, MeshModel mould_model, MeshModel parting_model) {
        List<MeshModel> models = [];
        try {
            var a = Boolean(mould_model, parting_model, BooleanOperation.Intersection).mesh;
            a = OffsetMesh(a, 0.5f);
            var b = Boolean(mould_model, a, BooleanOperation.DifferenceAB).mesh;

            a = Boolean(a, tool, BooleanOperation.DifferenceAB).mesh;
            b = Boolean(b, tool, BooleanOperation.DifferenceAB).mesh;
            
            models.Add( new(a));
            models.Add( new(b));
        } catch (Exception e) {
            return [];
        }

        return models.ToArray();
    }

    private static double DistanceSquared(this PointD point, PointD other) => 
        Math.Pow((point.x - other.x), 2) + Math.Pow((point.y - other.y), 2);

}
