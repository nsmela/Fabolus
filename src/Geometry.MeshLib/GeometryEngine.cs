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

    public IPartingTools PartingTools { get; }

    public GeometryEngine(IFileSystem fileSystem)
    {
        IO = new GeometryIO(fileSystem, this);
        Transforms = new GeometryTransforms(this);
        Booleans = new Booleans(this);
        Modifiers = new GeometryModifiers(this);
        Generators = new GeometryGenerators(this);
        Evaluators = new GeometryEvaluators(this);
        PartingTools = new PartingTools(this);
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

    /// <summary>
    /// Appends the given meshes' raw vertex/triangle data into a single mesh with disjoint
    /// components (no welding, no boolean union - callers are responsible for the sources not
    /// overlapping in space if that matters for what they do with the result afterward).
    /// </summary>
    public Result<IMesh> CombineMeshes(IEnumerable<IMesh> meshes)
    {
        var sources = (meshes ?? Array.Empty<IMesh>()).Where(m => m is not null && !m.IsEmpty).ToList();
        if (sources.Count == 0) return GeometryErrors.NullMesh;

        var vertices = new List<double>();
        var triangles = new List<int>();
        int vertexOffset = 0;

        foreach (var mesh in sources)
        {
            foreach (var v in mesh.Vertices)
            {
                vertices.Add(v.X);
                vertices.Add(v.Y);
                vertices.Add(v.Z);
            }

            foreach (var t in mesh.Triangles)
            {
                triangles.Add(t + vertexOffset);
            }

            vertexOffset += mesh.VertexCount;
        }

        var createResult = CreateMesh(vertices.ToArray().AsSpan(), triangles.ToArray().AsSpan());
        if (createResult.IsFailure) return createResult.Error;

        var metadata = createResult.Value.Metadata.WithProperties(m => m
            .Set(CoreKeys.Name, "Combined Mesh")
            .Set(CoreKeys.CreatedBy, "CombineMeshes"));

        return Result.Success(createResult.Value.WithMetadata(metadata));
    }

}
