using BasicResults;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
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
        var importResult = _geometryEngine.IO.Import(filePath);
        if (importResult.IsFailure)
            return importResult.Error;

        var initialMesh = importResult.Value;
        var importedMeshes = new List<IMesh>();

        var separatedResult = _geometryEngine.Evaluators.SeparateComponents(initialMesh);
        if (separatedResult.IsSuccess && separatedResult.Value.Length > 1) {
            importedMeshes.AddRange(separatedResult.Value);
        } else {
            importedMeshes.Add(initialMesh);
        }

        var currentWorkspace = workspace;
        Guid? firstId = null;

        foreach (var meshToProcess in importedMeshes) {
            var mesh = meshToProcess;

            // The engine names the mesh after the file it came from, and after the component
            // within it when one file held several.
            var record = MeshRecord.ForImport(mesh.Metadata.Name);

            // A mesh that arrives with its own command history (a Fabolus-saved 3mf) is already
            // in the frame its BaseMesh replays into, and that history carries the centring
            // TranslateCommand from when it was first imported. Centring it again would move the
            // geometry without moving the BaseMesh, so every replay-from-base view (smoothing,
            // rotate, export) would render it offset from what the viewport shows. Nothing
            // reconstructs a record from a file yet, so this is false for every import today.
            var hasOwnHistory = record.BaseMesh is not null || record.Commands.Any();

            if (!hasOwnHistory) {
                var statsResult = _geometryEngine.Evaluators.GetStatistics(mesh);
                if (statsResult.IsSuccess) {
                    var stats = statsResult.Value;
                    var centre = (stats.BoundsMin + stats.BoundsMax) / 2.0;
                    var centring = new TranslateCommand(new Vector3((float)-centre.X, (float)-centre.Y, (float)-centre.Z));

                    var transformResult = centring.Apply(_geometryEngine, mesh);
                    if (transformResult.IsSuccess) {
                        // Recorded rather than baked in: BaseMesh stays the pristine imported
                        // geometry, replay reproduces the centred mesh, and the offset from the
                        // authored position is persisted with the entry for later features to read.
                        // The stats measured just above are cached on it on the way past - the
                        // base mesh never changes, so anything comparing against it (the Smoothing
                        // panel's "Original Mesh" figures) reads them rather than measuring again.
                        record = record
                            .WithBaseMesh(mesh.WithAnnotations(new FabolusAnnotations(stats)))
                            .WithCommand(centring);
                        mesh = transformResult.Value;
                    }
                }
            }

            // Measured once here so every consumer sees a mesh that already knows its own bounds
            // and topology; IO validates on the way in, but the centring above invalidates the
            // bounds it measured.
            mesh = mesh.WithMeasurements(_geometryEngine);

            var addResult = currentWorkspace.AddMesh(mesh, record);
            if (addResult.IsFailure)
                return addResult.Error;

            currentWorkspace = addResult.Value;

            firstId ??= record.Id;
        }

        if (firstId.HasValue) {
            var activeResult = currentWorkspace.SetActiveMesh(firstId.Value);
            if (activeResult.IsFailure)
                return activeResult.Error;
            currentWorkspace = activeResult.Value;
        }

        return currentWorkspace;
    }
}
