using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

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

        var repairResult = fixSelfIntersections
            ? _geometryEngine.Modifiers.RepairSelfIntersections(mesh)
            : _geometryEngine.Modifiers.Repair(mesh);

        if (repairResult.IsFailure) return repairResult.Error;

        // Repair changes geometry (fills holes, removes degenerate faces), so both the topology
        // audit and the bounds have to be read again - consumers size UI from them.
        var repairedMesh = repairResult.Value.WithMeasurements(_geometryEngine);

        return workspace.UpdateMesh(meshId, repairedMesh);
    }
}
