using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using System.IO;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// Feature workflow for importing mesh files into the workspace.
/// </summary>
public sealed class ImportMesh {
    private readonly IGeometryEngine _geometryEngine;

    public ImportMesh(IGeometryEngine geometryEngine) {
        _geometryEngine = geometryEngine;
    }

    /// <summary>
    /// Imports a mesh file and adds it to the workspace.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace, string filePath) {
        // 1. Import mesh (metadata created automatically from filename)
        var importResult = _geometryEngine.IO.Import(filePath);
        if (importResult.IsFailure)
            return importResult.Error;

        var mesh = importResult.Value;

        // Center the mesh at the origin upon import
        var statsResult = _geometryEngine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsSuccess) {
            var s = statsResult.Value;
            double offsetX = -(s.MinX + s.MaxX) / 2.0;
            double offsetY = -(s.MinY + s.MaxY) / 2.0;
            double offsetZ = -(s.MinZ + s.MaxZ) / 2.0;

            if (Math.Abs(offsetX) > 0.001 || Math.Abs(offsetY) > 0.001 || Math.Abs(offsetZ) > 0.001) {
                var transformResult = _geometryEngine.Transforms.Translate(mesh, offsetX, offsetY, offsetZ);
                if (transformResult.IsSuccess)
                    mesh = transformResult.Value;
            }
        }

        // 2. Validate topology (IO already does this, but we ensure it's up to date)
        var validationResult = _geometryEngine.Evaluators.ValidateTopology(mesh);
        if (validationResult.IsSuccess) {
            if (validationResult.Value.HasCorruptTopology)
                return ConvertValidationToError(validationResult.Value, filePath);
            mesh = mesh.WithMetadata(mesh.Metadata.WithTopology(validationResult.Value));
        }

        // 4. Add to workspace (ID comes from mesh.Metadata.Id)
        var addResult = workspace.AddMesh(mesh);
        if (addResult.IsFailure)
            return addResult.Error;

        var newWorkspace = addResult.Value;

        // 5. Set imported mesh as active
        var activeResult = newWorkspace.SetActiveMesh(mesh.Metadata.Id);
        if (activeResult.IsFailure)
            return activeResult.Error;

        return activeResult.Value;
    }

    private static Error ConvertValidationToError(TopologyValidation validation, string filePath) {
        var fileName = Path.GetFileName(filePath);

        if (validation.HasCorruptTopology)
            return MeshErrors.ValidationFailed(fileName, "corrupt internal topology");

        if (!validation.IsWatertight)
            return MeshErrors.ValidationFailed(fileName, "not watertight");

        if (validation.HasOrphanedVertices)
            return MeshErrors.ValidationFailed(fileName, "contains orphaned vertices");

        if (validation.HasDegenerateTriangles)
            return MeshErrors.ValidationFailed(fileName, "contains degenerate triangles");

        return MeshErrors.ValidationFailed(fileName, "unknown reason");
    }
}
