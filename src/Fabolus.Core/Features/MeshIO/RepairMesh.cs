using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// Feature workflow for repairing mesh faults.
/// </summary>
public sealed class RepairMesh {
    private readonly IGeometryEngine _geometryEngine;

    public RepairMesh(IGeometryEngine geometryEngine) {
        _geometryEngine = geometryEngine;
    }

    public Result<Workspace> Execute(Workspace workspace, Guid meshId, bool fixSelfIntersections = false) {
        var meshResult = workspace.GetMesh(meshId);
        if (meshResult.IsFailure) return meshResult.Error;
        using var mesh = meshResult.Value;

        Result<IMesh> repairResult;
        if (fixSelfIntersections)
            repairResult = _geometryEngine.Modifiers.RepairSelfIntersections(mesh);
        else
            repairResult = _geometryEngine.Modifiers.Repair(mesh);


        if (repairResult.IsFailure) return repairResult.Error;
        var repairedMesh = repairResult.Value;

        // Re-audit after repair. Repair changes geometry (fills holes, removes degenerate
        // faces), so the cached Stats must be refreshed too - consumers size UI from them.
        var metadata = repairedMesh.Metadata;

        var audit = _geometryEngine.Evaluators.ValidateTopology(repairedMesh);
        if (audit.IsSuccess) metadata = metadata.WithTopology(audit.Value);

        var stats = _geometryEngine.Evaluators.GetStatistics(repairedMesh);
        if (stats.IsSuccess) metadata = metadata.WithMeshStats(stats.Value);

        repairedMesh = repairedMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(repairedMesh);
    }
}
