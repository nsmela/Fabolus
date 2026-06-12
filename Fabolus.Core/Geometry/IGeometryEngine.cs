using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Factory and operation interface for creating and manipulating geometry.
/// Implementations wrap specific geometry libraries (e.g., g3, Open3D, CGAL).
/// </summary>
public interface IGeometryEngine
{
    /// <summary>
    /// Provides transformation operations (translate, rotate, scale).
    /// </summary>
    IGeometryTransforms Transforms { get; }
    
    /// <summary>
    /// Provides import/export operations for mesh files.
    /// </summary>
    IGeometryIO IO { get; }
    
    /// <summary>
    /// Provides Boolean operations (union, intersection, difference).
    /// </summary>
    IBooleans Booleans { get; }

    /// <summary>
    /// Provides structural and topological modifications (e.g., smoothing).
    /// </summary>
    IGeometryModifiers Modifiers { get; }

    /// <summary>
    /// Provides procedural mesh generation operations (e.g., tubes, spheres).
    /// </summary>
    IGeometryGenerators Generators { get; }

    IGeometryEvaluators Evaluators { get; }

    /// <summary>
    /// Creates a mesh from raw vertex and triangle data.
    /// Performs sanitization (compaction, normal allocation).
    /// </summary>
    Result<IMesh> CreateMesh(ReadOnlySpan<double> vertices, ReadOnlySpan<int> triangles);
    
    /// <summary>
    /// Creates a deep copy of a mesh.
    /// </summary>
    Result<IMesh> CloneMesh(IMesh source);

}
