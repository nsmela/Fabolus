using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GeometryManifold.Internal.Native;

/// <summary>
/// Managed face of the libigl shim: marshalling, lifetime, and the choice of how to sign.
/// </summary>
/// <remarks>
/// Every entry point here is optional. When <see cref="GeometryNative.IsAvailable"/> is false the
/// callers fall back to <see cref="MeshBvh"/>, which computes the same field in managed code -
/// correctly, just slower - so a deployment without the shim degrades rather than breaks.
/// </remarks>
internal static class NativeDistanceField
{
    public static bool IsAvailable => GeometryNative.IsAvailable;

    /// <summary>
    /// Picks how to sign distances to this mesh.
    /// </summary>
    /// <remarks>
    /// A pseudonormal asks which side of the surface a point is on, which only means anything if
    /// the surface encloses something. On a mesh with boundary edges - a scan with holes in it,
    /// which is most of what arrives from a clinic - there is no inside to be on the wrong side
    /// of, and the answer is arbitrary. The winding number still works there: it measures how far
    /// the surface wraps around the point, which degrades gracefully as the surface opens up.
    ///
    /// So the closed case takes the fast path and the open case takes the one that is meaningful.
    /// </remarks>
    public static SignMode ChooseSignMode(IMesh mesh)
    {
        var edges = new HashSet<(int, int)>();
        var triangles = mesh.Triangles;

        // A boundary edge is one no second triangle walks back along. Toggling membership finds
        // them without counting: an edge left in the set at the end was seen an odd number of
        // times.
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            Toggle(edges, triangles[i], triangles[i + 1]);
            Toggle(edges, triangles[i + 1], triangles[i + 2]);
            Toggle(edges, triangles[i + 2], triangles[i]);
        }

        return edges.Count == 0 ? SignMode.PseudoNormal : SignMode.FastWindingNumber;
    }

    private static void Toggle(HashSet<(int, int)> edges, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!edges.Add(key)) edges.Remove(key);
    }

    /// <summary>
    /// Runs a whole offset natively, returning failure when the shim is absent so the caller can
    /// fall back rather than treating it as an error.
    /// </summary>
    public static Result<IMesh> Offset(
        IMesh input,
        float offsetDistance,
        Vector3 min,
        Vector3 max,
        double edgeLength,
        SignMode signMode,
        MeshMetadata metadata)
    {
        if (!GeometryNative.IsAvailable) return ManifoldErrors.Unavailable;

        var vertices = Flatten(input.Vertices);
        var triangles = input.Triangles;
        var bounds = new[] { (double)min.X, min.Y, min.Z, max.X, max.Y, max.Z };

        NativeMesh result = default;
        try
        {
            int status = GeometryNative.fabolus_offset(
                vertices,
                (nuint)input.VertexCount,
                triangles,
                (nuint)input.TriangleCount,
                offsetDistance,
                bounds,
                edgeLength,
                (int)signMode,
                out result);

            if (status != (int)NativeStatus.Ok) return NativeErrors.FromStatus((NativeStatus)status);

            return Extract(result, metadata);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return ManifoldErrors.Unavailable;
        }
        finally
        {
            if (result.Vertices != IntPtr.Zero || result.Triangles != IntPtr.Zero)
            {
                GeometryNative.fabolus_mesh_free(ref result);
            }
        }
    }

    /// <summary>
    /// Signed distance from every one of <paramref name="points"/> to <paramref name="mesh"/>,
    /// negative inside. One call for the whole batch; null when the shim is unavailable.
    /// </summary>
    public static double[]? QueryAll(IMesh mesh, IReadOnlyList<Vector3> points, SignMode signMode)
    {
        if (!GeometryNative.IsAvailable || points.Count == 0) return null;

        var sdf = IntPtr.Zero;
        try
        {
            sdf = GeometryNative.fabolus_sdf_create(
                Flatten(mesh.Vertices),
                (nuint)mesh.VertexCount,
                mesh.Triangles,
                (nuint)mesh.TriangleCount,
                (int)signMode);

            if (sdf == IntPtr.Zero) return null;

            var flat = new double[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                flat[i * 3] = points[i].X;
                flat[i * 3 + 1] = points[i].Y;
                flat[i * 3 + 2] = points[i].Z;
            }

            var results = new double[points.Count];
            int status = GeometryNative.fabolus_sdf_query(sdf, flat, (nuint)points.Count, results);

            return status == (int)NativeStatus.Ok ? results : null;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (sdf != IntPtr.Zero) GeometryNative.fabolus_sdf_destroy(sdf);
        }
    }

    private static double[] Flatten(Vector3[] vertices)
    {
        var flat = new double[vertices.Length * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            flat[i * 3] = vertices[i].X;
            flat[i * 3 + 1] = vertices[i].Y;
            flat[i * 3 + 2] = vertices[i].Z;
        }
        return flat;
    }

    private static Result<IMesh> Extract(NativeMesh native, MeshMetadata metadata)
    {
        int vertexCount = (int)native.VertexCount;
        int triangleCount = (int)native.TriangleCount;

        if (vertexCount == 0 || triangleCount == 0 ||
            native.Vertices == IntPtr.Zero || native.Triangles == IntPtr.Zero)
        {
            return ManifoldErrors.EmptyResult;
        }

        var flat = new double[vertexCount * 3];
        Marshal.Copy(native.Vertices, flat, 0, flat.Length);

        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3((float)flat[i * 3], (float)flat[i * 3 + 1], (float)flat[i * 3 + 2]);
        }

        // The shim hands back unsigned indices; a mesh large enough to overflow int would have
        // exhausted memory long before here, so the reinterpret is safe.
        var triangles = new int[triangleCount * 3];
        Marshal.Copy(native.Triangles, triangles, 0, triangles.Length);

        return Result.Success<IMesh>(new ManifoldMesh(vertices, triangles, metadata));
    }
}

/// <summary>Failures the shim can report.</summary>
internal static class NativeErrors
{
    public static Error FromStatus(NativeStatus status) => status switch
    {
        NativeStatus.ManifoldUnavailable => ManifoldErrors.Unavailable,
        NativeStatus.EmptyMesh => new Error(
            "Geometry.NativeEmptyMesh", "The mesh has no geometry to build a distance field from."),
        NativeStatus.LevelSetFailed => ManifoldErrors.EmptyResult,
        NativeStatus.InvalidArgument => new Error(
            "Geometry.NativeInvalidArgument", "The native distance field rejected its arguments."),
        _ => new Error("Geometry.NativeFailed", $"The native distance field failed: {status}."),
    };
}
