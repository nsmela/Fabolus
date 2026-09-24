using BasicResults;
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
    /// </summary>
    /// <param name="record">
    /// The workspace entry the geometry belongs to. Its history is what makes a saved mould
    /// reopen as a mould rather than as a raw bolus.
    /// </param>
    public Result Execute(IMesh mesh, MeshRecord record, string filePath, bool overwrite = false) {
        if (mesh is null)
            return MeshErrors.ExportMeshIsNull;

        if (string.IsNullOrWhiteSpace(filePath))
            return MeshErrors.ExportFilePathIsEmpty;

        if (record is null)
            return MeshErrors.ExportRecordIsNull;

        return FabolusPackage.IsPackage(filePath)
            ? ExportPackage(mesh, record, filePath, overwrite)
            : _geometryEngine.IO.Export(mesh, filePath, overwrite);
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

        try {
            File.WriteAllBytes(filePath, written.Value);
        } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) {
            return MeshErrors.ExportFailed(failure.Message);
        }

        return Result.Success();
    }
}
