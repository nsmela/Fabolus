using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Interface for mesh file I/O operations.
/// </summary>
public interface IGeometryIO
{
    /// <summary>
    /// Imports a mesh from a file.
    /// </summary>
    Result<IMesh> Import(string filePath);
    
    /// <summary>
    /// Exports a mesh to a file.
    /// </summary>
    Result Export(IMesh mesh, string filePath, bool overwrite = false);
}
