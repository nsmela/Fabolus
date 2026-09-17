using System.Numerics;
using BasicResults;
using Fabolus.Core.Common.Interfaces;
using GE = GeometryEngine.Core.Geometry;

namespace Fabolus.Core.Geometry.Engine;

/// <summary>
/// <see cref="IGeometryEngine"/> backed by the GeometryEngine library. Every operation is the
/// library's; what lives here is only what is Fabolus's own - converting its mesh and metadata
/// types, naming results the way features read them back, the error codes features and tests
/// match on, and the command history carried inside a 3MF.
/// </summary>
public sealed class GeometryEngineAdapter : IGeometryEngine
{
    public IGeometryTransforms Transforms { get; }
    public IGeometryIO IO { get; }
    public IBooleans Booleans { get; }
    public IGeometryModifiers Modifiers { get; }
    public IGeometryGenerators Generators { get; }
    public IGeometryEvaluators Evaluators { get; }
    public IPolygonOperations Polygons { get; }

    public GeometryEngineAdapter(IFileSystem fileSystem)
        : this(fileSystem, GeometryEngine.BspGeometryEngine.Create())
    {
    }

    internal GeometryEngineAdapter(IFileSystem fileSystem, GE.IGeometryEngine engine)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(engine);

        Evaluators = new EngineEvaluators(engine);
        IO = new EngineIO(fileSystem, engine, Evaluators);
        Transforms = new EngineTransforms(engine);
        Booleans = new EngineBooleans(engine);
        Modifiers = new EngineModifiers(engine);
        Generators = new EngineGenerators(engine);
        Polygons = new EnginePolygons(engine);
    }

    public Result<IMesh> CreateMesh(ReadOnlySpan<double> vertices, ReadOnlySpan<int> triangles)
    {
        if (vertices.Length % 3 != 0) return EngineErrors.InvalidVertexData;
        if (triangles.Length % 3 != 0) return EngineErrors.InvalidTriangleData;

        var points = new Vector3[vertices.Length / 3];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector3((float)vertices[i * 3], (float)vertices[i * 3 + 1], (float)vertices[i * 3 + 2]);
        }

        return Result.Success<IMesh>(new EngineMesh(
            points, triangles.ToArray(), EngineConversions.NewMetadata("Generated Mesh", "CreateMesh")));
    }

    public Result<IMesh> CloneMesh(IMesh source)
    {
        if (source is null) return MeshErrors.NullSource;

        return Result.Success<IMesh>(new EngineMesh(
            (Vector3[])source.Vertices.Clone(), (int[])source.Triangles.Clone(), source.Metadata));
    }
}

/// <summary>
/// The error codes Fabolus's features and tests match on. They predate GeometryEngine, which has
/// its own vocabulary; the adapter translates where a caller looks at the code.
/// </summary>
public static class EngineErrors
{
    public static readonly Error InvalidVertexData = new("Geometry.InvalidVertexData", "Vertex array length must be divisible by 3.");
    public static readonly Error InvalidTriangleData = new("Geometry.InvalidTriangleData", "Triangle array length must be divisible by 3.");
    public static readonly Error InvalidMesh = new("Geometry.InvalidMesh", "Mesh has invalid topology.");
    public static readonly Error InvalidScale = new("Geometry.InvalidScale", "Scale factors must be greater than zero.");
    public static readonly Error InvalidPath = new("Geometry.InvalidPath", "Tube path must contain at least two points.");
    public static readonly Error InvalidRadius = new("Geometry.InvalidRadius", "Radius must be greater than zero.");
    public static readonly Error InvalidRadii = new("Geometry.InvalidRadii", "Radii count must match the path point count.");
    public static readonly Error InvalidSegments = new("Geometry.InvalidSegments", "Segment count must be at least 3.");
    public static readonly Error InvalidDirection = new("Raycast.InvalidDirection", "Ray direction must be a non-zero vector.");

    public static Error HullFailed(string detail) => new("Geometry.HullFailed", detail);
    public static Error OffsetFailed(string detail) => new("Geometry.OffsetFailed", detail);
    public static Error UnionFailed(string detail) => new("Geometry.UnionFailed", detail);
    public static Error TriangulationFailed(string detail) => new("Geometry.TriangulationFailed", detail);

    public static Error FileNotFound(string path) => new("MRIO.FileNotFound", $"File not found: {path}");
    public static Error UnsupportedFormat(string extension) =>
        new("IO.UnsupportedFormat", $"Unsupported file format '{extension}'. Supported: .stl, .obj, .off, .ply, .3mf");
    public static Error ReadFailed(string message) => new("IO.ReadFailed", $"Failed to read mesh: {message}");
    public static Error FileExists(string path) => new("IO.FileExists", $"File '{path}' already exists. Set overwrite=true to replace.");
    public static Error WriteFailed(string message) => new("IO.WriteFailed", $"Failed to write mesh: {message}");
    public static Error AccessDenied(string path, string detail) => new("IO.AccessDenied", $"Access denied to '{path}': {detail}");

    /// <summary>Operation failures, keeping the library's own code and description.</summary>
    public static Error Failed(string operation, Error error) =>
        new($"Geometry.{operation}Failed", $"{error.Code}: {error.Description}");
}
