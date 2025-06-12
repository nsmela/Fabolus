using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Core.Meshes.PolygonTools;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Builders;

public sealed record TriangulatedMouldGenerator : MouldGenerator {
    public bool IsTight { get; private set; } = false;
    public bool HasTrough { get; private set; } = false; // whether to create a trough for excess silicone
    public double MaxHeight { get; private set; } = 10.0;
    public double MinHeight { get; private set; } = 0.0;
    public Polygon2d Contour { get; private set; } = new();

    public static TriangulatedMouldGenerator New() => new();
    public TriangulatedMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public TriangulatedMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public TriangulatedMouldGenerator WithContour(Polygon2d contour) => this with { Contour = contour };
    public TriangulatedMouldGenerator WithTightContour(bool isTight = true) => this with { IsTight = isTight, Contour = new() }; //resets the contour to empty to ensure recalculation
    public TriangulatedMouldGenerator WithTrough(bool hasTrough = true) => this with { HasTrough = hasTrough}; 
    public TriangulatedMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes.Select(tm => tm.Mesh).ToArray() };
    public TriangulatedMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public TriangulatedMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public override Result<MeshModel> Build() {
        var preview = Preview();

        if (preview.IsFailure) {
            return Result<MeshModel>.Fail(preview.Errors);
        }

        //create the mould
        var mould = BooleanOperators.Subtraction(preview.Data, BolusReference);
        if (mould.IsFailure) { return Result<MeshModel>.Fail(mould.Errors); }

        //convert the mesh tools
        DMesh3 tools = new();
        MeshEditor editor = new(new DMesh3());
        if (ToolMeshes.Count() > 0) {
            foreach (var mesh in ToolMeshes) {
                editor.AppendMesh(mesh);
            }

            MeshAutoRepair repair = new(editor.Mesh);
            repair.Apply();
            tools = new(repair.Mesh);
        }

        if (ToolMeshes is null || ToolMeshes.Count() == 0) { return Result<MeshModel>.Pass(new MeshModel(mould.Data)); }

        var result = BooleanOperators.Subtraction(mould.Data, tools);
        return new Result<MeshModel> { Data = new MeshModel(result.Data), IsSuccess = result.IsSuccess, Errors = result.Errors };
    }

    public override Result<MeshModel> Preview() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.CachedBounds.Min.z - OffsetBottom;

        // if done before, we can skip this step to save time
        if (Contour.IsEmpty()) {
            MeshEditor editor = new(new DMesh3());
            if (ToolMeshes.Count() > 0) {
                foreach (var m in ToolMeshes) {
                    editor.AppendMesh(m);
                }
            }
            editor.AppendMesh(BolusReference);

            if (IsTight) { Contour = MeshTools.ConcaveContour(editor.Mesh); }
            else { Contour = MeshTools.ConvexContour(editor.Mesh); }
        }

        if (Contour.IsEmpty()) {  return Result<MeshModel>.Fail(new MeshError("Contouring failed.")); }

        // apply offset
        Polygon2d contour = new(Contour);
        contour.PolyOffset(OffsetXY);
        if (contour.IsEmpty()) { return Result<MeshModel>.Fail(new MeshError("Contour offset failed.")); }

        // extrude mesh
        Result<DMesh3> result = PolygonTools.ExtrudePolygon(contour, MinHeight, MaxHeight);
        if (result.IsFailure) { return Result<MeshModel>.Fail(result.Errors); }
        DMesh3 extruded = result.Data;
        MeshAutoRepair repair = new(extruded);
        repair.Apply();

        if (HasTrough) {
            Result<DMesh3> trough = TroughtMesh();
            if (result.IsFailure) { return Result<MeshModel>.Fail([.. result.Errors, new MeshError("Failed to create trough")]); }

            result = BooleanOperators.Subtraction(extruded, trough.Data);
            if (result.IsFailure) { return Result<MeshModel>.Fail([.. result.Errors, new MeshError("Failed to subtract trough")]); }

            return Result<MeshModel>.Pass(new MeshModel(result.Data));
        }

        // return the mesh
        return Result<MeshModel>.Pass(new MeshModel(repair.Mesh));
    }

    /// <summary>
    /// Subtracts a space around the air channels at the top for room to store excess silicone while filling
    /// </summary>
    private Result<DMesh3> TroughtMesh() {
        var height = OffsetTop - 1.0f;
        var offset = OffsetXY - 2.5f;

        // generate the contoured mesh for the trough
        Polygon2d contour = new(Contour);
        contour.PolyOffset(offset);
        var result = PolygonTools.ExtrudePolygon(Contour, MaxHeight - height, MaxHeight + 0.1f);
        if (result.IsFailure) { return Result<DMesh3>.Fail([.. result.Errors, new MeshError("Failed to extrude polygon")]); }

        DMesh3 mesh = result.Data;

        MeshAutoRepair repair = new(mesh);
        repair.Apply();

        // move mould to min height
        return Result<DMesh3>.Pass(mesh);
    }
}

