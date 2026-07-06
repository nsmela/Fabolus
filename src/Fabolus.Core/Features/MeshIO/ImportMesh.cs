using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

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

        var initialMesh = importResult.Value;
        var importedMeshes = new List<IMesh>();

        var multipleCompsResult = _geometryEngine.Evaluators.HasMultipleComponents(initialMesh);
        if (multipleCompsResult.IsSuccess && multipleCompsResult.Value) {
            var separatedResult = _geometryEngine.Evaluators.SeparateComponents(initialMesh);
            if (separatedResult.IsSuccess)
                importedMeshes.AddRange(separatedResult.Value);
            else
                importedMeshes.Add(initialMesh);
        } else {
            importedMeshes.Add(initialMesh);
        }

        var currentWorkspace = workspace;
        Guid? firstId = null;

        foreach (var meshToProcess in importedMeshes) {
            var mesh = meshToProcess;

            // Center the mesh at the origin upon import and attach mesh stats. The stats are
            // attached even if centering fails - meshes without cached Stats force every
            // consumer to handle their absence, so only a stats failure itself leaves them out.
            var originalMetadata = mesh.Metadata;
            var statsResult = _geometryEngine.Evaluators.GetStatistics(mesh);
            if (statsResult.IsSuccess) {
                var stats = statsResult.Value;
                var centre = stats.Centre;

                var transformResult = _geometryEngine.Transforms.Translate(mesh, -centre.X, -centre.Y, -centre.Z);
                if (transformResult.IsSuccess) {
                    mesh = transformResult.Value;

                    var recomputed = _geometryEngine.Evaluators.GetStatistics(mesh);
                    if (recomputed.IsSuccess) stats = recomputed.Value;
                }

                // Built from the pre-translate metadata: the engine's Translate rewrites
                // Name/CreatedBy, which must not stick on an imported mesh.
                mesh = mesh.WithMetadata(originalMetadata.WithMeshStats(stats));
            }

            // Validate topology (IO already does this, but we ensure it's up to date)
            var validationResult = _geometryEngine.Evaluators.ValidateTopology(mesh);
            if (validationResult.IsSuccess) {
                mesh = mesh.WithMetadata(mesh.Metadata.WithTopology(validationResult.Value));
            }

            // Add to workspace (ID comes from mesh.Metadata.Id)
            var addResult = currentWorkspace.AddMesh(mesh);
            if (addResult.IsFailure)
                return addResult.Error;

            currentWorkspace = addResult.Value;

            if (firstId is null) firstId = mesh.Metadata.Id;
        }

        // 5. Set imported mesh as active (the first one)
        if (firstId.HasValue) {
            var activeResult = currentWorkspace.SetActiveMesh(firstId.Value);
            if (activeResult.IsFailure)
                return activeResult.Error;
            currentWorkspace = activeResult.Value;
        }

        return currentWorkspace;
    }

}
