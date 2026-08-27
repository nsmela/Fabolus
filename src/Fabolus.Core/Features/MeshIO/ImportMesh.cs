using Fabolus.Core.Common;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using System.Numerics;

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

        // A mesh carrying its own command history is a saved project rather than raw geometry, and
        // its meshes are deliberate: a combined cut is a single mesh even though the two halves it
        // holds are geometrically disconnected. Auto-separating multi-body geometry is only right
        // for raw imports (an STL with several bodies) - doing it to a saved project would break a
        // combined cut back into two meshes on reload. Same test the centring below uses.
        var initialMetadata = initialMesh.Metadata;
        var isSavedProject = initialMetadata.HasBaseMesh || initialMetadata.Commands.Any();

        if (!isSavedProject) {
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
            var metadata = mesh.Metadata;

            // A mesh that arrives with its own command history (a Fabolus-saved 3mf) is already
            // in the frame its BaseMesh replays into, and that history carries the centring
            // TranslateCommand from when it was first imported. Centring it again would move the
            // geometry without moving the BaseMesh, so every replay-from-base view (smoothing,
            // rotate, export) would render it offset from what the viewport shows.
            var hasOwnHistory = metadata.HasBaseMesh || metadata.Commands.Any();

            var statsResult = _geometryEngine.Evaluators.GetStatistics(mesh);
            if (statsResult.IsSuccess) {
                var stats = statsResult.Value;

                if (!hasOwnHistory) {
                    var centre = stats.Centre;
                    var centring = new TranslateCommand(new Vector3(-centre.X, -centre.Y, -centre.Z));

                    var transformResult = centring.Apply(_geometryEngine, mesh);
                    if (transformResult.IsSuccess) {
                        // Recorded rather than baked in: BaseMesh stays the pristine imported
                        // geometry, replay reproduces the centred mesh, and the offset from the
                        // authored position is persisted with the file for later features to read.
                        metadata = metadata.WithBaseMesh(mesh).WithCommand(centring);
                        mesh = transformResult.Value;

                        var recomputed = _geometryEngine.Evaluators.GetStatistics(mesh);
                        if (recomputed.IsSuccess) stats = recomputed.Value;
                    }
                }

                // Built from the pre-translate metadata: the engine's Translate rewrites
                // Name/CreatedBy, which must not stick on an imported mesh.
                mesh = mesh.WithMetadata(metadata.WithMeshStats(stats));
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
