using Fabolus.Core.Common;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// Feature workflow for exporting a mesh to a file. A parting result is a single mesh whose two halves
/// are only geometrically separated; how it lands on disk depends on the file type and the recorded
/// <see cref="PartingResultMode"/>:
/// <list type="bullet">
/// <item>3MF is a project file - always one file, carrying the command recipe (including the mode).</item>
/// <item>A mesh format (STL, OBJ, ...) writes one file when combined, and one file per half
/// (<c>name_A</c>, <c>name_B</c>, ...) when separated.</item>
/// </list>
/// </summary>
public sealed class ExportMesh {
    private readonly IGeometryEngine _geometryEngine;

    public ExportMesh(IGeometryEngine geometryEngine) {
        _geometryEngine = geometryEngine;
    }

    /// <summary>
    /// Exports the given mesh to the specified file path (or, for a separated mesh-format export, to
    /// one <c>name_&lt;letter&gt;</c> file per half beside it). Pass <c>overwrite: true</c> when the
    /// caller (e.g. SaveFileDialog) has already confirmed replacing an existing file.
    /// </summary>
    public Result Execute(IMesh mesh, string filePath, bool overwrite = false) {
        if (mesh is null)
            return MeshErrors.ExportMeshIsNull;

        if (string.IsNullOrWhiteSpace(filePath))
            return MeshErrors.ExportFilePathIsEmpty;

        var is3mf = Path.GetExtension(filePath).Equals(".3mf", StringComparison.OrdinalIgnoreCase);
        var mode = mesh.Metadata.Commands.OfType<CutCommand>().FirstOrDefault()?.Mode ?? PartingResultMode.Combined;

        // 3MF carries the whole project (recipe + mode) in a single file, so the separated/combined
        // choice is deferred until it is re-exported to a mesh format. Combined always writes one file.
        // Only a separated mesh-format export splits into a file per half.
        if (is3mf || mode == PartingResultMode.Combined)
            return _geometryEngine.IO.Export(mesh, filePath, overwrite);

        var componentsResult = _geometryEngine.Evaluators.SeparateComponents(mesh);
        if (componentsResult.IsFailure)
            return componentsResult.Error;

        var components = componentsResult.Value.ToList();

        // A separated result should have a component per half; if it somehow has one, there is nothing
        // to split, so fall back to a single file rather than writing a lone "_A".
        if (components.Count < 2)
            return _geometryEngine.IO.Export(mesh, filePath, overwrite);

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        for (int i = 0; i < components.Count; i++) {
            var partPath = Path.Combine(directory, $"{name}_{(char)('A' + i)}{extension}");
            var export = _geometryEngine.IO.Export(components[i], partPath, overwrite);
            if (export.IsFailure)
                return export.Error;
        }

        return Result.Success();
    }
}
