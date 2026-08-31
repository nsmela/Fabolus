using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Records that a mesh is a mould cut in place - the parting mesh subtracted, leaving the two halves
/// joined in a single mesh (separated only by the parting-mesh gap). Stored as the recipe (line +
/// parting-mesh parameters) rather than baked geometry, so replaying re-runs the cut against whatever
/// the earlier commands in the chain produced. <see cref="Mode"/> is the user's export intent and does
/// not affect the geometry this produces - it is carried here so it survives a 3MF round trip.
/// </summary>
public sealed record CutCommand(
    PartingLineParameters LineParameters,
    PartingMeshParameters MeshParameters,
    PartingResultMode Mode) : IMeshCommand
{
    public int Priority => CommandPriority.Split;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var mouldResult = MouldMesh.Create(mesh);
        if (mouldResult.IsFailure) return mouldResult.Error;

        return new PartingMeshFeature(engine).CutMould(mouldResult.Value, LineParameters, MeshParameters);
    }
}
