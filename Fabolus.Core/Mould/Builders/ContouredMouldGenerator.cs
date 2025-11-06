using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Mould.Builders;

public sealed record ContouredMouldGenerator : MouldGenerator {
    public float Offset { get; init; } = 2.0f;
    private MeshModel _preview;

    public static ContouredMouldGenerator New() => new();
    public ContouredMouldGenerator WithOffset(double offset) => this with { Offset = (float)offset };
    public ContouredMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public ContouredMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes };

    public override Result<MeshModel> Build() {
        var mould = MeshTools.BooleanSubtraction(_preview, BolusReference);
        if (mould.IsFailure) { return mould.Errors; }

        DMesh3 tools = new();
        MeshEditor editor = new(new DMesh3());
        if (ToolMeshes.Any())
        {
            foreach (var mesh in ToolMeshes)
            {
                editor.AppendMesh(mesh.Mesh);
            }

            MeshAutoRepair repair = new(editor.Mesh);
            repair.Apply();
            tools = new(repair.Mesh);
        }

        if (ToolMeshes is null || !ToolMeshes.Any()) { return mould.Data; } // dont remove the tools

        return MeshTools.BooleanSubtraction(mould.Data.Mesh.ToMesh(), tools.ToMesh());
    }

    public override Result<MeshModel> Preview() {
        Mesh bolus = (Mesh)BolusReference;
        Mesh model = MeshTools.OffsetMesh(bolus, Offset);

        _preview = new(model); // store to make calculations quicker
        return _preview;
    }
}
