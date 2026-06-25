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
        var mesh = meshResult.Value;

        Result<IMesh> repairResult;
        if (fixSelfIntersections) 
            repairResult = _geometryEngine.Modifiers.RepairSelfIntersections(mesh);
        else 
            repairResult = _geometryEngine.Modifiers.Repair(mesh);
        

        if (repairResult.IsFailure) return repairResult.Error;
        var repairedMesh = repairResult.Value;

        // Re-audit after repair
        var audit = _geometryEngine.Evaluators.ValidateTopology(repairedMesh);
        if (audit.IsSuccess) repairedMesh = repairedMesh.WithMetadata(repairedMesh.Metadata.WithTopology(audit.Value));

        return workspace.UpdateMesh(repairedMesh);
    }
}
