using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using IMesh = Fabolus.Core.Geometry.IMesh;

namespace GeometryManifold;

/// <summary>
/// <see cref="IGeometryEngine"/> built on Manifold (via ManifoldNET) instead of MeshLib.
/// Drop-in replacement for GeometryMeshLib.GeometryEngine: same interface, same errors, same
/// metadata contract.
/// </summary>
public sealed class GeometryEngine : IGeometryEngine
{
    public IGeometryIO IO { get; }
    public IGeometryTransforms Transforms { get; }
    public IBooleans Booleans { get; }
    public IGeometryModifiers Modifiers { get; }
    public IGeometryGenerators Generators { get; }
    public IGeometryEvaluators Evaluators { get; }
    public IPolygonOperations Polygons { get; }

    public GeometryEngine(IFileSystem fileSystem)
    {
        IO = new GeometryIO(fileSystem, this);
        Transforms = new GeometryTransforms(this);
        Booleans = new Booleans(this);
        Modifiers = new GeometryModifiers(this);
        Generators = new GeometryGenerators(this);
        Evaluators = new GeometryEvaluators(this);
        Polygons = new Polygons(this);
    }

    public Result<IMesh> CreateMesh(ReadOnlySpan<double> vertices, ReadOnlySpan<int> triangles)
    {
        if (vertices.Length % 3 != 0) return GeometryErrors.InvalidVertexData;
        if (triangles.Length % 3 != 0) return GeometryErrors.InvalidTriangleData;

        try
        {
            var vectors = new Vector3[vertices.Length / 3];
            for (int i = 0; i < vertices.Length; i += 3)
            {
                vectors[i / 3] = new Vector3((float)vertices[i], (float)vertices[i + 1], (float)vertices[i + 2]);
            }

            var tris = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                tris[i] = triangles[i];
            }

            var metadata = new MeshMetadata().WithProperties(m =>
                m.Set(CoreKeys.Id, Guid.NewGuid())
                 .Set(CoreKeys.Name, "Generated Mesh")
                 .Set(CoreKeys.CreatedBy, "CreateMesh"));

            return Result.Success<IMesh>(new ManifoldMesh(vectors, tris, metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.CreateFailed", ex.Message);
        }
    }

    public Result<IMesh> CloneMesh(IMesh source) =>
        Result.Success<IMesh>(new ManifoldMesh(source.Vertices, source.Triangles, source.Metadata));
}
