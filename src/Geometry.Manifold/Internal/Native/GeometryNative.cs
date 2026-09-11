using System.Runtime.InteropServices;

namespace GeometryManifold.Internal.Native;

/// <summary>How the sign of a distance is decided. Mirrors FabolusSignMode in fabolus_geometry.h.</summary>
internal enum SignMode
{
    /// <summary>Angle-weighted pseudonormal. Fast, and exact on a closed mesh.</summary>
    PseudoNormal = 0,

    /// <summary>
    /// Fast winding number. Slower, but meaningful on open or self-intersecting input, where a
    /// pseudonormal is not - which is most of what comes out of a patient scanner.
    /// </summary>
    FastWindingNumber = 1,
}

/// <summary>Status codes from the shim. Mirrors FabolusStatus.</summary>
internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = -1,
    EmptyMesh = -2,
    ManifoldUnavailable = -3,
    LevelSetFailed = -4,
    Exception = -5,
}

/// <summary>A mesh owned by the shim. Mirrors FabolusMesh; released with fabolus_mesh_free.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMesh
{
    public IntPtr Vertices;
    public nuint VertexCount;
    public IntPtr Triangles;
    public nuint TriangleCount;
}

/// <summary>
/// P/Invoke surface for fabolus_geometry, the native distance-field shim built on libigl.
/// </summary>
/// <remarks>
/// The shim exists because Manifold's level-set mesher asks for a signed distance one point at a
/// time. Answering those from managed code costs a P/Invoke transition per sample, and a bolus
/// offset is a few hundred thousand samples - the transitions, not the field, are the expense.
/// So the whole offset is handed over in one call and the per-sample work never leaves native
/// code.
///
/// It is optional. Everything it does has a managed implementation behind
/// <see cref="MeshBvh"/>, and <see cref="IsAvailable"/> is false when the binary is absent, so a
/// build without it is slower rather than broken.
/// </remarks>
internal static class GeometryNative
{
    private const string LibraryName = "fabolus_geometry";

    /// <summary>
    /// Must match FABOLUS_GEOMETRY_ABI_VERSION. A shim that disagrees is treated as absent: it is
    /// a stale binary beside a newer assembly, and calling into it would marshal against the
    /// wrong struct layout.
    /// </summary>
    private const int ExpectedAbiVersion = 1;

    /// <summary>Whether the shim is present, loadable, and built against the ABI this expects.</summary>
    public static bool IsAvailable => AvailableLazy.Value;

    private static readonly Lazy<bool> AvailableLazy = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    static GeometryNative()
    {
        NativeLibraryResolver.Register(LibraryName, ProbePaths);
    }

    private static IEnumerable<string> ProbePaths() => NativeLibraryResolver.ProbePaths(LibraryName);

    private static bool Probe()
    {
        if (!ProbePaths().Any(File.Exists)) return false;

        try
        {
            return fabolus_geometry_abi_version() == ExpectedAbiVersion;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int fabolus_geometry_abi_version();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fabolus_mesh_free(ref NativeMesh mesh);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr fabolus_sdf_create(
        [In] double[] vertices, nuint vertexCount, [In] int[] triangles, nuint triangleCount, int signMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fabolus_sdf_destroy(IntPtr sdf);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int fabolus_sdf_query(
        IntPtr sdf, [In] double[] points, nuint count, [Out] double[] results);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int fabolus_offset(
        [In] double[] vertices,
        nuint vertexCount,
        [In] int[] triangles,
        nuint triangleCount,
        double offsetDistance,
        [In] double[] bounds,
        double edgeLength,
        int signMode,
        out NativeMesh result);
}
