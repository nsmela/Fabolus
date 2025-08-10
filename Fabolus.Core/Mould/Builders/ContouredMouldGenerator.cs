using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Mould.Builders;

public sealed record ContouredMouldGenerator : MouldGenerator {
    public float Offset { get; init; } = 2.0f;

    public static ContouredMouldGenerator New() => new();
    public ContouredMouldGenerator WithOffset(double offset) => this with { Offset = (float)offset };
    public ContouredMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public ContouredMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes };

    public override Result<MeshModel> Build() {
        throw new NotImplementedException();
    }

    public override Result<MeshModel> Preview() {
        Mesh bolus = (Mesh)BolusReference;
        Mesh model = MeshTools.OffsetMesh(bolus, Offset);
        var boolean_response = MeshTools.BooleanSubtraction(model, bolus);

        return new MeshModel(model);
    }
}
