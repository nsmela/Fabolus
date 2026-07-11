using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Geometry.MeshLib;
using System.Numerics;
using IMesh = Fabolus.Core.Geometry.IMesh;

namespace GeometryMeshLib;

public sealed class GeometryEngine : IGeometryEngine
{
    public IGeometryIO IO { get; }
    public IGeometryTransforms Transforms { get; }
    public IBooleans Booleans { get; }
    public IGeometryModifiers Modifiers { get; }
    public IGeometryGenerators Generators { get; }

    public IGeometryEvaluators Evaluators { get; }

    public GeometryEngine(IFileSystem fileSystem)
    {
        IO = new GeometryIO(fileSystem, this);
        Transforms = new GeometryTransforms(this);
        Booleans = new Booleans(this);
        Modifiers = new GeometryModifiers(this);
        Generators = new GeometryGenerators(this);
        Evaluators = new GeometryEvaluators(this);
    }

    internal Result<IMesh> CreateMesh(MR.Mesh mesh, MeshMetadata metadata)
    {
        if (mesh is null) return GeometryErrors.NullMesh;
        if (metadata is null) return GeometryErrors.NullMetadata;

        return Result.Success(mesh.ToIMesh(metadata));
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

            return Result.Success<IMesh>(new MRMesh(vectors, tris, metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.CreateFailed", ex.Message);
        }
    }

    public Result<IMesh> CloneMesh(IMesh source)
    {
        return Result.Success<IMesh>(new MRMesh(source.Vertices, source.Triangles, source.Metadata));
    }


}
