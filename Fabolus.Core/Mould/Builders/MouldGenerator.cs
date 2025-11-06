using Fabolus.Core.Meshes;
using g3;

namespace Fabolus.Core.Mould.Builders;

/// <summary>
/// The inherited class for all mould generators. Immutable.
/// </summary>
public abstract record MouldGenerator {
    protected MeshModel BolusReference { get; init; } //mesh to invert while entirely within

    protected MeshModel[] ToolMeshes { get; init; } = []; // mesh to boolean subtract from the mold

    public abstract Result<MeshModel> Build();
    public abstract Result<MeshModel> Preview();
}
