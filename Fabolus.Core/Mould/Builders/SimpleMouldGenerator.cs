using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Mould.Utils;
using g3;
using gs;
using System.Windows.Media.Media3D;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using static MR.DotNet;

namespace Fabolus.Core.Mould.Builders;

/// <summary>
/// Generates a mould based on the inflated silhouette of the mesh and then extruded along the z axis to encase the entire supplied mesh.
/// </summary>
public sealed record SimpleMouldGenerator : MouldGenerator {
    //ref: https://github.com/NewWheelTech/geometry4Sharp/blob/ea3c96d0e437989eb49923ccc72088a6947c69a9/mesh_ops/MeshPlaneCut.cs#L230

    private Vector2d[] Contour { get; set; } = [];
    private DMesh3? PreviewMesh { get; set; }

    public double MaxHeight { get; private set; } = 10.0;
    public double MinHeight { get; private set; } = 0.0;

    public static SimpleMouldGenerator New() => new();
    public SimpleMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public SimpleMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus, Contour = [] };
    public SimpleMouldGenerator WithOffsets(double offset) => this with { OffsetTop = offset, OffsetBottom = offset, OffsetXY = offset };
    public SimpleMouldGenerator WithCalculationResolution(int resolution) => this with { CalculationResolution = resolution };
    public SimpleMouldGenerator WithContourResolution(double resolution) => this with { ContourResolution = resolution };
    public SimpleMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes.Select( tm => tm.Mesh).ToArray(), Contour = [] };
    public SimpleMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public SimpleMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public override Result<MeshModel> Build() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }
        if (PreviewMesh is null) { 
            Preview(); 
            if (PreviewMesh is null) { throw new NullReferenceException("Build: Preview mesh is null"); }
        }

        var result = BooleanOperators.Subtraction(PreviewMesh, BolusReference);

        if (result.IsFailure) { return Result<MeshModel>.Fail(result.Errors); }
        if (ToolMeshes is null || ToolMeshes.Count() == 0) { return Result<MeshModel>.Pass(new MeshModel(result.Mesh)); }

        MeshEditor editor = new(new DMesh3());
        foreach (var mesh in ToolMeshes) {
            editor.AppendMesh(mesh);
        }

        MeshAutoRepair repair = new(editor.Mesh);
        repair.Apply();
        DMesh3 tools = repair.Mesh;

        var reply = BooleanOperators.Subtraction(result.Mesh, tools);
        return new Result<MeshModel> { Mesh = new MeshModel(reply.Mesh), IsSuccess = reply.IsSuccess, Errors = reply.Errors};
    }

    public override Result<MeshModel> Preview() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.CachedBounds.Min.z - OffsetBottom;

        //generate the inflated mesh
        SetContour();

        PreviewMesh = CalculateSilhouette();

        //create the mould
        return Result<MeshModel>.Pass(new MeshModel(PreviewMesh));
    }

    private void SetContour() {
        if (BolusReference is null || BolusReference.TriangleCount == 0) {
            Contour = [];
            return; 
        }

        if (Contour.Length > 0) { return; }

        //var contour = MeshSilhouette.MeshToSilhouette(BolusReference);
        var contour = MeshSilhouette.SilhouetteFromMesh(BolusReference);
        Contour = contour.Select(v => new Vector2d(v.x, v.y)).ToArray();
    }

    private DMesh3 CalculateSilhouette() {
        //inflate the Contour
        //var contour = MeshSilhouette.InflateSilhouette(Contour, OffsetXY);
        var contour = MeshSilhouette.InflateContour(Contour, OffsetXY);

        //create polygon
        var verts = contour.Select(v => new Vertex(v.x, v.y)).ToArray();
        var polygon = new Polygon();
        polygon.Add(new Contour(verts));

        DMesh3 result = new();

        List<Vector3d> bottomLoop = new();
        List<int> bottomLoopIndices = new();

        Vector3d lower = Vector3d.AxisZ * MinHeight;
        Vector3d upper = Vector3d.AxisZ * MaxHeight;

        foreach (var t in new GenericMesher().Triangulate(polygon).Triangles) {
            //add verts
            var p0 = t.GetVertex(0);
            var l0 = result.AppendVertex(ToVector3d(p0, MinHeight));
            var b0 = result.AppendVertex(ToVector3d(p0, MaxHeight));

            var p1 = t.GetVertex(1);
            var l1 = result.AppendVertex(ToVector3d(p1, MinHeight));
            var b1 = result.AppendVertex(ToVector3d(p1, MaxHeight));

            var p2 = t.GetVertex(2);
            var l2 = result.AppendVertex(ToVector3d(p2, MinHeight));
            var b2 = result.AppendVertex(ToVector3d(p2, MaxHeight));

            //link those verts to triangles
            result.AppendTriangle(l0, l1, l2);
            result.AppendTriangle(b2, b1, b0);

        }

        //sides
        DMesh3 sides = new();

        List<int> lowerLoop = new();
        List<int> upperLoop = new();
        foreach (var v in verts) {
            //add vertex and record the index id
            lowerLoop.Add(sides.AppendVertex(ToVector3d(v, MinHeight)));
            upperLoop.Add(sides.AppendVertex(ToVector3d(v, MaxHeight)));
        }

        int n = contour.Length - 1;
        for (int i = 0; i < 10; ++i) {
            var p0 = lowerLoop[i];
            var p1 = lowerLoop[i + 1];
            var p2 = upperLoop[i];
            var p3 = upperLoop[i + 1];

            sides.AppendTriangle(p0, p1, p2);
            sides.AppendTriangle(p1, p3, p2);
        }

        MeshEditor editor = new(result);
        editor.AppendMesh(sides);

        MeshAutoRepair repair = new(editor.Mesh);
        repair.Apply();

        return repair.Mesh;
    }

    private static Vector3d ToVector3d(Vertex v, double z) => new Vector3d(v.X, v.Y, z);
}
