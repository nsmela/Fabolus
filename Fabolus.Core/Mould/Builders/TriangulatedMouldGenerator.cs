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
    public double OffsetXY { get; private set; } = 3.0;
    public double OffsetTop { get; private set; } = 2.0;
    public double OffsetBottom { get; private set; } = 2.0;
    public bool IsTight { get; private set; } = true;
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
    public TriangulatedMouldGenerator WithToolMeshes(IEnumerable<MeshModel> toolMeshes) => this with { ToolMeshes = toolMeshes.ToArray() };
    public TriangulatedMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public TriangulatedMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public override Result<MeshModel> Build() {
        var preview = Preview();

        if (preview.IsFailure) { return preview.Errors; }

        //create the mould
        var mould = MeshTools.BooleanSubtraction(preview.Data, BolusReference);
        if (mould.IsFailure) { return mould.Errors; }

        //convert the mesh tools
        DMesh3 tools = new();
        MeshEditor editor = new(new DMesh3());
        if (ToolMeshes.Count() > 0) {
            foreach (var mesh in ToolMeshes) {
                editor.AppendMesh(mesh.Mesh);
            }

            MeshAutoRepair repair = new(editor.Mesh);
            repair.Apply();
            tools = new(repair.Mesh);
        }

        if (ToolMeshes is null || ToolMeshes.Count() == 0) { return mould.Data; }

        var result = MeshTools.BooleanSubtraction(mould.Data.Mesh, tools);
        return new Result<MeshModel> { Data = result.Data, IsSuccess = result.IsSuccess, Errors = result.Errors };
    }

    public override Result<MeshModel> Preview() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.Mesh.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.Mesh.CachedBounds.Min.z - OffsetBottom;

        // if done before, we can skip this step to save time
        if (Contour.IsEmpty()) {
            MeshEditor editor = new(new DMesh3());
            if (ToolMeshes.Count() > 0) {
                foreach (var m in ToolMeshes) {
                    editor.AppendMesh(m.Mesh);
                }
            }
            editor.AppendMesh(BolusReference.Mesh);

            if (IsTight) { Contour = MeshTools.ConcaveContour(editor.Mesh); }
            else { Contour = MeshTools.ConvexContour(editor.Mesh); }
        }

        if (Contour.IsEmpty()) {  return new MeshError("Contouring failed."); }

        // apply offset
        Polygon2d contour = new(Contour);
        contour.PolyOffset(OffsetXY);
        if (contour.IsEmpty()) { return new MeshError("Contour offset failed."); }

        // extrude mesh
        Result<DMesh3> result = PolygonTools.ExtrudePolygon(contour, MinHeight, MaxHeight);
        if (result.IsFailure) { return result.Errors; }
        DMesh3 extruded = result.Data;
        MeshAutoRepair repair = new(extruded);
        repair.Apply();

        if (HasTrough) {
            Result<DMesh3> trough = TroughtMesh();
            if (result.IsFailure) {
                List<MeshError> errors = [.. result.Errors, new MeshError("Failed to subtract trough")];
                return errors; 
            }

            var mesh_result = MeshTools.BooleanSubtraction(extruded, trough.Data);
            if (mesh_result.IsFailure) {
                List<MeshError> errors = [.. result.Errors, new MeshError("Failed to subtract trough")];
                return errors; 
            }

            return mesh_result.Data!;
        }

        // return the mesh
        return new MeshModel(repair.Mesh);
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
        if (result.IsFailure) {
            List<MeshError> errors = [.. result.Errors, new MeshError("Failed to extrude polygon")];
            return errors;
        }

        DMesh3 mesh = result.Data!;

        MeshAutoRepair repair = new(mesh);
        repair.Apply();

        // move mould to min height
        return mesh;
    }
}

