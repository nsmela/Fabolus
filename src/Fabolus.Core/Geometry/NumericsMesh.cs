using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fabolus.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A mesh as the parting code reads it: single-precision <see cref="System.Numerics.Vector3"/>
/// vertices and a plain triangle array.
/// </summary>
/// <remarks>
/// <para>
/// The parting line, the band graph and the ridge detector were written against
/// <c>System.Numerics</c> in single precision, and their thresholds were measured that way on the
/// asset set. They are carried across to GeometryEngine as they are rather than rewritten in
/// <see cref="GeometryEngine.Core.Geometry.Primitives.Vec3"/>, so the numbers they were tuned on still
/// hold; this is the one place a mesh crosses from the engine's double-precision vertices into theirs.
/// </para>
/// <para>
/// Meshes are immutable, so the instance identifies the geometry, and the table is weak on the mesh:
/// the conversion is made once per mesh however many of the parting steps read it, and goes when the
/// mesh does.
/// </para>
/// </remarks>
public sealed class NumericsMesh
{
    private static readonly ConditionalWeakTable<IMesh, NumericsMesh> Converted = new();

    public Vector3[] Vertices { get; }

    /// <summary>The engine's own triangle array, shared rather than copied.</summary>
    public int[] Triangles { get; }

    public int VertexCount => Vertices.Length;

    public int TriangleCount => Triangles.Length / 3;

    private NumericsMesh(IMesh mesh)
    {
        var source = mesh.Vertices;
        var vertices = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
            vertices[i] = new Vector3((float)source[i].X, (float)source[i].Y, (float)source[i].Z);

        Vertices = vertices;
        Triangles = ImmutableCollectionsMarshal.AsArray(mesh.Triangles) ?? [];
    }

    public static NumericsMesh Of(IMesh mesh) => Converted.GetValue(mesh, m => new NumericsMesh(m));
}
