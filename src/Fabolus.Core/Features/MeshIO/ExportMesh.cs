using BasicResults;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Collections.Immutable;
// Only the package type, not the namespace: it also holds a MeshErrors that would collide with
// Fabolus's own.
using MeshPackage = GeometryEngine.Core.Geometry.MeshPackage;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// Feature workflow for exporting a mesh to a file.
/// </summary>
public sealed class ExportMesh {
    private readonly IGeometryEngine _geometryEngine;

    public ExportMesh(IGeometryEngine geometryEngine) {
        _geometryEngine = geometryEngine;
    }

    /// <summary>
    /// Exports the given mesh to the specified file path.
    /// Pass <c>overwrite: true</c> when the caller (e.g. SaveFileDialog) has already
    /// confirmed with the user that an existing file should be replaced.
    ///
    /// A 3MF is written as a package: the geometry, the entry's pristine base mesh as a second
    /// non-printable object, and the command history as vendor metadata. Every other format holds
    /// geometry alone, so exporting a mould to STL keeps the shape and drops the history - that is
    /// what those formats are, and the caller chose one.
    ///
    /// Succeeds with the paths actually written: the one named, or, for a separated parting cut,
    /// the lettered file per piece written in its place.
    /// </summary>
    /// <param name="record">
    /// The workspace entry the geometry belongs to. Its history is what makes a saved mould
    /// reopen as a mould rather than as a raw bolus.
    /// </param>
    /// <param name="overwrite">
    /// Whether <paramref name="filePath"/> itself may be replaced. It covers that one name only.
    /// </param>
    /// <param name="confirmReplace">
    /// Asked, with the paths, before a separated parting export replaces files the user never
    /// named: the save dialog only ever saw "bolus.stl", not the "bolus_A.stl" and "bolus_B.stl"
    /// actually written. Returning false, or passing nothing, leaves them alone and the export
    /// fails with <see cref="MeshErrors.ExportCancelled"/> or <see cref="MeshErrors.ExportFilesExist"/>.
    /// </param>
    public Result<IReadOnlyList<string>> Execute(
        IMesh mesh,
        MeshRecord record,
        string filePath,
        bool overwrite = false,
        Func<IReadOnlyList<string>, bool>? confirmReplace = null) {
        if (mesh is null)
            return MeshErrors.ExportMeshIsNull;

        if (string.IsNullOrWhiteSpace(filePath))
            return MeshErrors.ExportFilePathIsEmpty;

        if (record is null)
            return MeshErrors.ExportRecordIsNull;

        if (FabolusPackage.IsPackage(filePath))
            return Written(ExportPackage(mesh, record, filePath, overwrite), filePath);

        // A parting cut kept as one mesh can still have been asked for as separate pieces. The
        // package carries that choice with the rest of the recipe, so it is only acted on here,
        // when the cut leaves for a format that holds nothing but geometry.
        var mode = record.Command<CutCommand>()?.Mode ?? PartingResultMode.Combined;
        return mode == PartingResultMode.Combined
            ? Written(_geometryEngine.IO.Export(mesh, filePath, overwrite), filePath)
            : ExportPieces(mesh, filePath, overwrite, confirmReplace);
    }

    private static Result<IReadOnlyList<string>> Written(Result export, params string[] paths) =>
        export.IsFailure ? export.Error : Result.Success<IReadOnlyList<string>>(paths);

    /// <summary>
    /// Writes each piece of <paramref name="mesh"/> to a file of its own, lettered beside
    /// <paramref name="filePath"/>, biggest first. A piece is a component as thick as a mould wall
    /// (see <see cref="PartingPieces"/>): any sliver the cut left is left out rather than written as
    /// a file of its own. A mesh that is only one piece is written as it is rather than as a lone
    /// "_A".
    /// </summary>
    private Result<IReadOnlyList<string>> ExportPieces(
        IMesh mesh, string filePath, bool overwrite, Func<IReadOnlyList<string>, bool>? confirmReplace) {
        var componentsResult = _geometryEngine.Evaluators.SeparateComponents(mesh);
        if (componentsResult.IsFailure)
            return componentsResult.Error;

        var components = PartingPieces.Substantial(componentsResult.Value);
        if (components.Count < 2)
            return Written(_geometryEngine.IO.Export(mesh, filePath, overwrite), filePath);

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var partPaths = Enumerable.Range(0, components.Count)
            .Select(i => Path.Combine(directory, $"{name}_{(char)('A' + i)}{extension}"))
            .ToList();

        // Checked for the whole set before any piece is written, so a refusal never leaves half
        // the pieces new and half from an earlier export.
        var existing = partPaths.Where(File.Exists).ToList();
        if (existing.Count > 0) {
            if (confirmReplace is null)
                return MeshErrors.ExportFilesExist(existing);

            if (!confirmReplace(existing))
                return MeshErrors.ExportCancelled;
        }

        for (int i = 0; i < components.Count; i++) {
            var export = _geometryEngine.IO.Export(components[i], partPaths[i], overwrite: true);
            if (export.IsFailure)
                return export.Error;
        }

        return Result.Success<IReadOnlyList<string>>(partPaths);
    }

    private Result ExportPackage(IMesh mesh, MeshRecord record, string filePath, bool overwrite) {
        if (File.Exists(filePath) && !overwrite)
            return MeshErrors.ExportFileExists(filePath);

        var metadata = ImmutableDictionary<string, string>.Empty;

        if (record.Commands.Count > 0) {
            metadata = metadata.Add(
                FabolusPackage.CommandsKey,
                MeshCommandSerializer.Serialize(record.Commands));
        }

        // The base mesh rides along as the package's reference object: replaying the history needs
        // the geometry it replays against, and without it a reopened file could show its commands
        // but not undo any of them.
        var reference = record.BaseMesh is null
            ? Maybe<IMesh>.None()
            : Maybe<IMesh>.Some(record.BaseMesh);

        var written = _geometryEngine.IO.WritePackage(
            new MeshPackage(mesh, reference, metadata, FabolusPackage.Vendor));

        if (written.IsFailure) return written.Error;

        // A package is the project itself - the only copy of its history - so it is never
        // truncated in place.
        return SafeFile.WriteAllBytes(filePath, written.Value);
    }
}
