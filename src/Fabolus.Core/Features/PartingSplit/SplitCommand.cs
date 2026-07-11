using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Records that a mesh is one half of a mould split along a parting line. The parting line
/// itself is frozen in at the time the split was applied (it was computed from the base body,
/// not the mould - see <see cref="SplitMouldFeature"/>), so replaying this command only needs
/// to rebuild the tool solid and re-run a single boolean against whatever mould geometry the
/// earlier commands in the chain produced.
/// </summary>
public sealed record SplitCommand(PartingLine PartingLine, Vector3 PullDirection, PartingSide Side) : IMeshCommand
{
    public int Priority => CommandPriority.Split;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (!PartingLine.IsValid) return MeshErrors.InvalidPartingLine;

        var boundsResult = engine.Evaluators.GetStatistics(mesh);
        if (boundsResult.IsFailure) return boundsResult.Error;

        var toolResult = engine.PartingTools.GenerateSplitTool(mesh, PartingLine, PullDirection, boundsResult.Value);
        if (toolResult.IsFailure) return toolResult.Error;

        var tool = toolResult.Value;

        return Side == PartingSide.Positive
            ? engine.Booleans.Intersect(mesh, tool)
            : engine.Booleans.Subtract(mesh, tool);
    }
}
