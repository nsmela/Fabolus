using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

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
    /// </summary>
    public Result Execute(IMesh mesh, string filePath, bool overwrite = false) {
        if (mesh is null)
            return MeshErrors.ExportMeshIsNull;

        if (string.IsNullOrWhiteSpace(filePath))
            return MeshErrors.ExportFilePathIsEmpty;

        return _geometryEngine.IO.Export(mesh, filePath, overwrite);
    }
}
