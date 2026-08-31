using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Records that a mesh is one half of a mould split, as the recipe that produced it rather than as
/// baked geometry: the parting line's parameters (including the pull direction), the parting mesh's
/// parameters, and which half this is.
///
/// Replaying re-traces the line and rebuilds the parting mesh against whatever geometry the earlier
/// commands in the chain produced, then keeps this command's <see cref="Side"/>. That means an
/// upstream change - a different smoothing strength, a re-generated mould - flows through to the
/// split, which is the point of storing the recipe instead of a frozen line.
/// </summary>
public sealed record SplitCommand(
    PartingLineParameters LineParameters,
    PartingMeshParameters MeshParameters,
    PartingSide Side) : IMeshCommand
{
    public int Priority => CommandPriority.Split;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var mouldResult = MouldMesh.Create(mesh);
        if (mouldResult.IsFailure) return mouldResult.Error;

        var splitResult = new PartingMeshFeature(engine).SplitMould(mouldResult.Value, LineParameters, MeshParameters);
        if (splitResult.IsFailure) return splitResult.Error;

        return Side == PartingSide.Positive
            ? Result.Success(splitResult.Value.Positive)
            : Result.Success(splitResult.Value.Negative);
    }
}
