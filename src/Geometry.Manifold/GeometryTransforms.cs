using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;

namespace GeometryManifold;

/// <summary>
/// Rigid and uniform transforms. These run on the plain vertex array rather than through
/// Manifold: a transform cannot break topology, and routing it through a native handle would
/// reject the open meshes the app legitimately carries mid-pipeline.
/// </summary>
internal sealed class GeometryTransforms : IGeometryTransforms
{
    private readonly GeometryEngine _engine;

    public GeometryTransforms(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IMesh> Translate(IMesh source, double deltaX, double deltaY, double deltaZ)
    {
        var delta = new Vector3((float)deltaX, (float)deltaY, (float)deltaZ);
        var vertices = new Vector3[source.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = source.Vertices[i] + delta;
        }

        var metadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Translated ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Translate({deltaX}, {deltaY}, {deltaZ})"));

        return Result.Success<IMesh>(new ManifoldMesh(vertices, (int[])source.Triangles.Clone(), metadata));
    }

    public Result<IMesh> Scale(IMesh source, double scaleFactor) =>
        Scale(source, scaleFactor, scaleFactor, scaleFactor);

    public Result<IMesh> Scale(IMesh source, double scaleX, double scaleY, double scaleZ)
    {
        if (scaleX <= 0 || scaleY <= 0 || scaleZ <= 0)
            return GeometryErrors.InvalidScale;

        var scale = new Vector3((float)scaleX, (float)scaleY, (float)scaleZ);
        var vertices = new Vector3[source.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = source.Vertices[i] * scale;
        }

        var metadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Scaled ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Scale({scaleX}, {scaleY}, {scaleZ})"));

        return Result.Success<IMesh>(new ManifoldMesh(vertices, (int[])source.Triangles.Clone(), metadata));
    }

    public Result<IMesh> Rotate(IMesh source, Quaternion q)
    {
        // Normalised so a caller's slightly-off quaternion rotates without also scaling.
        var rotation = Quaternion.Normalize(q);

        var vertices = new Vector3[source.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Vector3.Transform(source.Vertices[i], rotation);
        }

        return Result.Success<IMesh>(new ManifoldMesh(vertices, (int[])source.Triangles.Clone(), source.Metadata));
    }
}
