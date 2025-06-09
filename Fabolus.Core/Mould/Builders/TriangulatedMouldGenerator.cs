using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Builders;

public sealed record TriangulatedMouldGenerator : MouldGenerator {
    public double MaxHeight { get; private set; } = 10.0;
    public double MinHeight { get; private set; } = 0.0;

    public static TriangulatedMouldGenerator New() => new();
    public TriangulatedMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public TriangulatedMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public TriangulatedMouldGenerator WithOffsets(double offset) => this with { OffsetTop = offset, OffsetBottom = offset, OffsetXY = offset };
    public TriangulatedMouldGenerator WithContourResolution(double resolution) => this with { ContourResolution = resolution };
    public TriangulatedMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes.Select(tm => tm.Mesh).ToArray() };
    public TriangulatedMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public TriangulatedMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public override Result<MeshModel> Build() {
        throw new NotImplementedException();
    }

    public override Result<MeshModel> Preview() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.CachedBounds.Min.z - OffsetBottom;

        var contour = MeshTools.OutlineContour(BolusReference, OffsetXY);
        var mesh = MeshTools.TriangulateContour(contour, MinHeight);

        // extrude mesh
        var m = MeshTools.ExtrudeMesh(mesh, MaxHeight - MinHeight);
        MeshAutoRepair repair = new(m);
        repair.Apply();

        // move mould to min height
        MeshTransforms.Translate(repair.Mesh, new Vector3d(0, 0, MinHeight));

        // return the mesh
        return Result<MeshModel>.Pass(new MeshModel(repair.Mesh));
    }
}

