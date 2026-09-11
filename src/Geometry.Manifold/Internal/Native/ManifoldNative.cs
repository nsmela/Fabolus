using System.Reflection;
using System.Runtime.InteropServices;

namespace GeometryManifold.Internal.Native;

/// <summary>Why Manifold refused to build a solid from the mesh it was given.</summary>
internal enum ManifoldStatus
{
    NoError = 0,
    NonFiniteVertex = 1,
    NotManifold = 2,
    VertexIndexOutOfBounds = 3,
    PropertiesWrongLength = 4,
    MissingPositionProperties = 5,
    MergeVectorsDifferentLengths = 6,
    MergeIndexOutOfBounds = 7,
    TransformWrongLength = 8,
    RunIndexWrongLength = 9,
    FaceIdWrongLength = 10,
    InvalidConstruction = 11,
}

/// <summary>
/// P/Invoke surface for Manifold's C API (manifold 3.x), bound directly rather than through a
/// wrapper package.
/// </summary>
/// <remarks>
/// Written against the C API rather than taken from NuGet on purpose. The ManifoldNET binding's
/// Dispose frees the handle block with FreeHGlobal but never calls the matching
/// manifold_delete_*, so the C++ destructor never runs and every solid leaks its vertex and
/// triangle buffers - not survivable in an app that runs booleans all session. It also binds the
/// float MeshGL rather than the double-precision MeshGL64 this uses.
///
/// The 64-bit entry points are the ones taken throughout: the app's meshes carry millimetre
/// coordinates on parts tens of millimetres across, and rounding those to float on the way in and
/// out of every boolean is avoidable error.
/// </remarks>
internal static unsafe class ManifoldNative
{
    private const string LibraryName = "manifoldc";

    /// <summary>
    /// Whether the native library could be found on this machine.
    /// </summary>
    /// <remarks>
    /// Asked before any P/Invoke so a host without the binaries gets a described failure rather
    /// than a <see cref="DllNotFoundException"/> thrown out of the middle of an operation. It is
    /// evaluated once: whether the library is present cannot change while the process runs.
    /// </remarks>
    public static bool IsAvailable => AvailableLazy.Value;

    private static readonly Lazy<bool> AvailableLazy = new(
        () => ProbePaths().Any(File.Exists),
        LazyThreadSafetyMode.ExecutionAndPublication);

    static ManifoldNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(ManifoldNative).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName) return IntPtr.Zero;

        foreach (var path in ProbePaths())
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Every location the native library might sit in, most specific first. The assembly's own
    /// directory and the host's base directory are not the same thing - deployed into a plug-in
    /// folder the natives travel with the assembly - so both are searched.
    /// </summary>
    private static IEnumerable<string> ProbePaths()
    {
        foreach (var root in Roots())
        {
            yield return Path.Combine(root, "manifoldc.dll");
            yield return Path.Combine(root, "libmanifoldc.so");
            yield return Path.Combine(root, "manifoldc.so");
            yield return Path.Combine(root, "libmanifoldc.dylib");

            foreach (var rid in new[] { "win-x64", "win-arm64" })
            {
                yield return Path.Combine(root, "runtimes", rid, "native", "manifoldc.dll");
            }

            foreach (var rid in new[] { "linux-x64", "linux-arm64" })
            {
                yield return Path.Combine(root, "runtimes", rid, "native", "libmanifoldc.so");
            }

            foreach (var rid in new[] { "osx-arm64", "osx-x64" })
            {
                yield return Path.Combine(root, "runtimes", rid, "native", "libmanifoldc.dylib");
            }
        }
    }

    private static IEnumerable<string> Roots()
    {
        // Assembly.Location is empty in a single-file publish, hence the guard.
        var assemblyDirectory = Path.GetDirectoryName(typeof(ManifoldNative).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            yield return assemblyDirectory;
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) &&
            !string.Equals(
                baseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                assemblyDirectory?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            yield return baseDirectory;
        }
    }

    /// <summary>Signed distance callback for the level-set mesher. Positive inside the solid.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate double SdfCallback(double x, double y, double z, IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    public struct Vec2
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vec3
    {
        public double X;
        public double Y;
        public double Z;
    }

    // ===== Allocation and destruction =====
    // Every manifold_alloc_* hands back a block the matching manifold_delete_* must free; the
    // delete runs the C++ destructor as well, which is what releases the geometry inside.

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_alloc_manifold();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void manifold_delete_manifold(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_alloc_manifold_vec();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void manifold_delete_manifold_vec(IntPtr ms);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_alloc_meshgl64();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void manifold_delete_meshgl64(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_alloc_simple_polygon();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void manifold_delete_simple_polygon(IntPtr p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_alloc_polygons();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void manifold_delete_polygons(IntPtr p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_alloc_box();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void manifold_delete_box(IntPtr b);

    // ===== Mesh construction and extraction =====

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_meshgl64(
        IntPtr mem, double* vertProps, nuint nVerts, nuint nProps, ulong* triVerts, nuint nTris);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_meshgl64_merge(IntPtr mem, IntPtr mesh);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_of_meshgl64(IntPtr mem, IntPtr mesh);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_get_meshgl64(IntPtr mem, IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_meshgl64_num_vert(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_meshgl64_num_tri(IntPtr m);

    /// <summary>
    /// How many properties each vertex carries - at least three for the position, but a mesh may
    /// carry more, so this is the stride through the interleaved buffer.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_meshgl64_num_prop(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_meshgl64_vert_properties_length(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_meshgl64_tri_length(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double* manifold_meshgl64_vert_properties(IntPtr mem, IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong* manifold_meshgl64_tri_verts(IntPtr mem, IntPtr m);

    // ===== Booleans =====

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_union(IntPtr mem, IntPtr a, IntPtr b);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_difference(IntPtr mem, IntPtr a, IntPtr b);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_intersection(IntPtr mem, IntPtr a, IntPtr b);

    // ===== Construction and transforms =====

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_extrude(
        IntPtr mem, IntPtr polygons, double height, int slices, double twistDegrees, double scaleX, double scaleY);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_translate(IntPtr mem, IntPtr m, double x, double y, double z);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_decompose(IntPtr mem, IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_manifold_vec_length(IntPtr ms);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_manifold_vec_get(IntPtr mem, IntPtr ms, nuint index);

    // ===== Polygons =====

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_simple_polygon(IntPtr mem, Vec2* points, nuint length);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_polygons(IntPtr mem, IntPtr* simplePolygons, nuint length);

    // ===== Level set =====

    /// <summary>
    /// Meshes the isosurface where the field equals <c>level</c>. Manifold keeps the region where
    /// the field is greater, so the field must read positive inside the solid.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_level_set(
        IntPtr mem, SdfCallback sdf, IntPtr bounds, double edgeLength, double level, double tolerance, IntPtr context);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr manifold_box(
        IntPtr mem, double x1, double y1, double z1, double x2, double y2, double z2);

    // ===== Diagnostics =====

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int manifold_is_empty(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ManifoldStatus manifold_status(IntPtr m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint manifold_num_tri(IntPtr m);
}
