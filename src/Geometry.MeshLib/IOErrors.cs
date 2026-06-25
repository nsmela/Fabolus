using Fabolus.Core.Common;

namespace GeometryMeshLib;

/// <summary>
/// Centralized error registry for I/O operations in MRGeometryEngine.
/// </summary>
internal static class IOErrors
{
    public static Error FileNotFound(string path) =>
        new("MRIO.FileNotFound", $"File not found: {path}");

    public static Error UnsupportedFormat(string extension, IEnumerable<string> supported) =>
        new("IO.UnsupportedFormat",
            $"Unsupported file format '{extension}'. Supported: {string.Join(", ", supported)}");

    public static Error ReadFailed(string message) =>
        new("IO.ReadFailed", $"Failed to read mesh: {message}");

    public static readonly Error NoMeshData =
        new("IO.NoMeshData", "No mesh data found in file.");

    public static readonly Error InvalidMeshType =
        new("IO.InvalidMeshType", "Expected MRMesh instance for export.");

    public static Error FileExists(string path) =>
        new("IO.FileExists", $"File '{path}' already exists. Set overwrite=true to replace.");

    public static Error WriteFailed(string message) =>
        new("IO.WriteFailed", $"Failed to write mesh: {message}");

    public static Error AccessDenied(string path, string detail) =>
        new("IO.AccessDenied", $"Access denied to '{path}': {detail}");

    public static Error WriteException(string detail) =>
        new("IO.ExportException", $"Unexpected error exporting mesh: {detail}");
}
