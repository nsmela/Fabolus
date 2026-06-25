using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Geometry.MeshLib;
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
        Booleans = new Booleans();
        Modifiers = new GeometryModifiers(this);
        Generators = new GeometryGenerators(this);
        Evaluators = new GeometryEvaluators(this);
    }

    internal Result<IMesh> CreateMesh(MR.Mesh mesh, MeshMetadata metadata, IMesh? originalMesh = null)
    {
        if (mesh is null) return GeometryErrors.NullMesh;
        if (metadata is null) return GeometryErrors.NullMetadata;

        return new MRMesh(mesh, metadata, originalMesh);
    }

    public Result<IMesh> CreateMesh(ReadOnlySpan<double> vertices, ReadOnlySpan<int> triangles)
    {
        if (vertices.Length % 3 != 0) return GeometryErrors.InvalidVertexData;
        if (triangles.Length % 3 != 0) return GeometryErrors.InvalidTriangleData;

        try
        {
            var mesh = new MR.Mesh();
            ulong maxVid = (ulong)(vertices.Length / 3);
            mesh.points.vec.resize(maxVid);

            for (int i = 0; i < vertices.Length; i += 3)
            {
                mesh.points.vec[(ulong)(i / 3)] = new MR.Vector3f((float)vertices[i], (float)vertices[i + 1], (float)vertices[i + 2]);
            }

            var vertTriples = new MR.Std.Vector_MRVertId();
            vertTriples.resize((ulong)triangles.Length);
            for (int i = 0; i < triangles.Length; i++)
            {
                vertTriples[(ulong)i] = new MR.VertId(triangles[i]);
            }

            MR.MeshBuilder.addTriangles(mesh.topology, vertTriples, null);
            mesh.invalidateCaches();

            var metadata = new MeshMetadata().WithProperties(m =>
                m.Set(CoreKeys.Id, Guid.NewGuid())
                 .Set(CoreKeys.Name, "Generated Mesh")
                 .Set(CoreKeys.CreatedBy, "CreateMesh"));

            return new MRMesh(mesh, metadata);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.CreateFailed", ex.Message);
        }
    }

    public Result<IMesh> CloneMesh(IMesh source)
    {
        if (source is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        return Result.Success<IMesh>(mrMesh.Clone());
    }


}
