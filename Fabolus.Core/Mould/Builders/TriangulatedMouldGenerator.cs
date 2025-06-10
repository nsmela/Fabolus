using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Core.Mould.Utils;
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
    public List<double[]> Contour { get; private set; } = [];

    public static TriangulatedMouldGenerator New() => new();
    public TriangulatedMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public TriangulatedMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public TriangulatedMouldGenerator WithContour(List<double[]> contour) => this with { Contour = contour };
    public TriangulatedMouldGenerator WithTightContour(bool isTight = true) => this with { IsTight = isTight, Contour = [] }; //resets the contour to empty to ensure recalculation
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

        if (HasTrough) {
            result = BooleanOperators.Subtraction(result.Data, TroughtMesh());
        }

        return new Result<MeshModel> { Data = new MeshModel(result.Data), IsSuccess = result.IsSuccess, Errors = result.Errors };
    }

    public override Result<MeshModel> Preview() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.CachedBounds.Min.z - OffsetBottom;

        // if done before, we can skip this step to save time
        if (Contour.Count() == 0) {
            MeshEditor editor = new(new DMesh3());
            if (ToolMeshes.Count() > 0) {
                foreach (var m in ToolMeshes) {
                    editor.AppendMesh(m);
                }
            }
            editor.AppendMesh(BolusReference);

            if (IsTight) { Contour = MeshTools.TightContour(editor.Mesh); }
            else { Contour = MeshTools.OutlineContour(editor.Mesh); }
            
        }

        var contour = MeshTools.ContourOffset(Contour, OffsetXY);
        var mesh = MeshTools.TriangulateContour(contour, MinHeight);

        // extrude mesh
        var extruded = MeshTools.ExtrudeMesh(mesh, MaxHeight - MinHeight);
        MeshAutoRepair repair = new(extruded);
        repair.Apply();

        // return the mesh
        return Result<MeshModel>.Pass(new MeshModel(repair.Mesh));
    }

    /// <summary>
    /// Subtracts a space around the air channels at the top for room to store excess silicone while filling
    /// </summary>
    private DMesh3 TroughtMesh() {
        var height = OffsetTop - 1.0f;
        var offset = OffsetXY - 2.5f;

        // generate the contoured mesh for the trough
        var contour = MeshTools.ContourOffset(Contour, offset);
        var mesh = MeshTools.TriangulateContour(contour, MaxHeight - height + 0.1f);

        // extrude mesh
        var extruded = MeshTools.ExtrudeMesh(mesh, height);
        MeshAutoRepair repair = new(extruded);
        repair.Apply();

        // move mould to min height
        return repair.Mesh;
    }
}

