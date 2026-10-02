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
        // A Fabolus-written 3MF carries the entry it was saved from. Read that first: it comes
        // back as one entry with its own history, not as raw geometry to be centred and split.
        if (FabolusPackage.IsPackage(filePath)) {
            var saved = ImportSaved(workspace, filePath);
            if (saved.HasValue) return saved.Value;
        }

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

            // Measured before anything is derived from it. This is the base mesh every replay
            // starts from, and the engine hands its measurements through each translation and
            // rotation - so a history of moves alone never measures anything again, and the
            // Smoothing panel's "Original Mesh" figures are read rather than computed.
            mesh = mesh.Measured(_geometryEngine);

            if (mesh.Stats(_geometryEngine) is { } stats) {
                var centre = (stats.BoundsMin + stats.BoundsMax) / 2.0;
                var centring = new TranslateCommand(new Vector3((float)-centre.X, (float)-centre.Y, (float)-centre.Z));

                var transformResult = centring.Apply(_geometryEngine, mesh);
                if (transformResult.IsSuccess) {
                    // Recorded rather than baked in: BaseMesh stays the pristine imported
                    // geometry, replay reproduces the centred mesh, and the offset from the
                    // authored position is persisted with the entry for later features to read.
                    record = record
                        .WithBaseMesh(mesh)
                        .WithCommand(centring);
                    mesh = transformResult.Value;
                }
            }

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

    /// <summary>
    /// Restores a workspace entry from a Fabolus-written package, or None when the file is a 3MF
    /// that Fabolus did not write - one from a scanner or a slicer is ordinary geometry and takes
    /// the path above.
    ///
    /// A restored entry is deliberately not re-centred. It is already in the frame its base mesh
    /// replays into, and that history carries the centring translation from when it was first
    /// imported; centring it again would move the geometry without moving the base, so every
    /// replay-from-base view would draw the model offset from what the viewport shows.
    /// </summary>
    private Maybe<Result<Workspace>> ImportSaved(Workspace workspace, string filePath) {
        byte[] bytes;
        try {
            bytes = File.ReadAllBytes(filePath);
        } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) {
            return Maybe<Result<Workspace>>.Some(MeshErrors.ExportFailed(failure.Message));
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        var package = _geometryEngine.IO.ReadPackage(bytes, name, FabolusPackage.Vendor);

        // Not a package this build can read - fall through and treat it as plain geometry.
        if (package.IsFailure) return Maybe<Result<Workspace>>.None();

        var contents = package.Value;
        if (!contents.Metadata.TryGetValue(FabolusPackage.CommandsKey, out var json)) {
            return Maybe<Result<Workspace>>.None();
        }

        // Past this point the file claims to be ours and to carry a history, so a failure to read
        // it is reported rather than quietly downgraded to a geometry-only import - that would
        // open the file looking correct while having silently forgotten what it is.
        var commands = MeshCommandSerializer.Deserialize(json);
        if (commands.IsFailure) return Maybe<Result<Workspace>>.Some(commands.Error);

        var mesh = contents.Model.Measured(_geometryEngine);

        // Measured on the way past, the way the geometry import measures the base mesh it keeps,
        // so replays from it start with the measurements already taken.
        var baseMesh = (contents.Reference.HasValue ? contents.Reference.Value : contents.Model)
            .Measured(_geometryEngine);

        var record = MeshRecord.ForImport(name) with {
            Commands = commands.Value,
            // Falls back to the model when the package carries no reference object: the history
            // then replays from the saved geometry, which is wrong for undo but better than an
            // entry that cannot replay at all.
            BaseMesh = baseMesh,
        };

        var added = workspace.AddMesh(mesh, record);
        if (added.IsFailure) return Maybe<Result<Workspace>>.Some(added.Error);

        return Maybe<Result<Workspace>>.Some(added.Value.SetActiveMesh(record.Id));
    }
}
