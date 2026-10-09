using System.Collections.Immutable;
using BasicResults;

namespace Fabolus.Tests.Fixtures;

/// <summary>
/// Builds engine meshes from the flat coordinate arrays the parting tests generate their test shapes
/// in - x, y, z per vertex, as the engine they were written against took them.
/// </summary>
public static class FlatMeshExtensions
{
    public static Result<IMesh> MeshFrom(
        this IGeometryEngine engine, double[] coordinates, int[] triangles, string name = "test")
    {
        var vertices = ImmutableArray.CreateBuilder<Vector3>(coordinates.Length / 3);
        for (int i = 0; i + 2 < coordinates.Length; i += 3)
            vertices.Add(new Vector3(coordinates[i], coordinates[i + 1], coordinates[i + 2]));

        return engine.CreateMesh(vertices.MoveToImmutable(), triangles.ToImmutableArray(), MeshMetadata.Named(name));
    }
}
