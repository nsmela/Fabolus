using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GeometryManifold.Internal.Native;

/// <summary>How a result was produced, so a caller can tell what it is holding.</summary>
internal enum ManifoldProvenance
{
    /// <summary>The operands were valid 2-manifolds; the result is guaranteed watertight.</summary>
    Native,

    /// <summary>
    /// An operand was not a valid 2-manifold and was welded by Manifold's own merge before the
    /// operation. The result is still watertight, but it describes geometry that differs from
    /// what was handed in.
    /// </summary>
    NativeAfterMergingOperands,
}

/// <summary>The outcome of a native operation, with the provenance of the mesh it produced.</summary>
internal readonly record struct ManifoldOutcome(IMesh Mesh, ManifoldProvenance Provenance);

/// <summary>
/// Drives Manifold's C API: marshals to and from <see cref="IMesh"/> and frees every native
/// resource deterministically, so no solid outlives the operation that made it.
/// </summary>
/// <remarks>
/// No failure escapes as an exception. The engine's contract is that every failure is a
/// <see cref="Result"/>, and a P/Invoke breaks that contract loudly: a missing or mismatched
/// native binary raises <see cref="DllNotFoundException"/> or
/// <see cref="EntryPointNotFoundException"/> from the first call, which would tear down the
/// caller rather than be reported.
/// </remarks>
internal static unsafe class ManifoldKernel
{
    private delegate IntPtr BooleanOperation(IntPtr mem, IntPtr a, IntPtr b);

    public static Result<ManifoldOutcome> Union(IMesh left, IMesh right, MeshMetadata metadata) =>
        RunBoolean(left, right, metadata, ManifoldNative.manifold_union);

    public static Result<ManifoldOutcome> Subtract(IMesh left, IMesh right, MeshMetadata metadata) =>
        RunBoolean(left, right, metadata, ManifoldNative.manifold_difference);

    public static Result<ManifoldOutcome> Intersect(IMesh left, IMesh right, MeshMetadata metadata) =>
        RunBoolean(left, right, metadata, ManifoldNative.manifold_intersection);

    /// <summary>
    /// Re-meshes the isosurface of a signed distance field, the counterpart of MeshLib's
    /// voxel-based offsetMesh.
    /// </summary>
    /// <param name="signedDistance">Distance to the surface, negative inside the solid.</param>
    /// <param name="level">Distance from the surface the new one should sit at.</param>
    public static Result<IMesh> LevelSet(
        Func<Vector3, float> signedDistance,
        Vector3 min,
        Vector3 max,
        double edgeLength,
        double level,
        MeshMetadata metadata)
    {
        return Guarded(() =>
        {
            // Manifold keeps the region where the field is above the level, so the field is
            // handed over negated: positive inside the solid.
            ManifoldNative.SdfCallback callback =
                (x, y, z, _) => -signedDistance(new Vector3((float)x, (float)y, (float)z));

            var box = ManifoldNative.manifold_alloc_box();
            var solid = IntPtr.Zero;
            try
            {
                ManifoldNative.manifold_box(box, min.X, min.Y, min.Z, max.X, max.Y, max.Z);

                solid = ManifoldNative.manifold_alloc_manifold();
                ManifoldNative.manifold_level_set(solid, callback, box, edgeLength, -level, 0, IntPtr.Zero);

                // The callback must outlive the native call; without this the JIT is free to
                // collect the delegate while Manifold is still sampling through it.
                GC.KeepAlive(callback);

                var status = ManifoldNative.manifold_status(solid);
                if (status != ManifoldStatus.NoError) return ManifoldErrors.OperationFailed(status);
                if (ManifoldNative.manifold_num_tri(solid) == 0) return ManifoldErrors.EmptyResult;

                return Extract(solid, metadata);
            }
            finally
            {
                if (solid != IntPtr.Zero) ManifoldNative.manifold_delete_manifold(solid);
                ManifoldNative.manifold_delete_box(box);
            }
        });
    }

    /// <summary>
    /// Extrudes 2D contours into a solid along +Z from the origin. Manifold triangulates the caps
    /// and closes the walls itself, so the result is manifold by construction.
    /// </summary>
    /// <param name="contours">Outer boundaries wound counter-clockwise, holes clockwise.</param>
    public static Result<IMesh> Extrude(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        double height,
        double zOffset,
        MeshMetadata metadata)
    {
        return Guarded(() =>
        {
            var simplePolygons = new List<IntPtr>(contours.Count);
            var polygons = IntPtr.Zero;
            var extruded = IntPtr.Zero;
            var placed = IntPtr.Zero;

            try
            {
                foreach (var contour in contours)
                {
                    if (contour.Count < 3) continue;

                    var points = new ManifoldNative.Vec2[contour.Count];
                    for (int i = 0; i < contour.Count; i++)
                    {
                        points[i] = new ManifoldNative.Vec2 { X = contour[i].X, Y = contour[i].Y };
                    }

                    var polygon = ManifoldNative.manifold_alloc_simple_polygon();
                    fixed (ManifoldNative.Vec2* pPoints = points)
                    {
                        ManifoldNative.manifold_simple_polygon(polygon, pPoints, (nuint)points.Length);
                    }
                    simplePolygons.Add(polygon);
                }

                if (simplePolygons.Count == 0) return ManifoldErrors.EmptyOperand("extrusion contours");

                var handles = simplePolygons.ToArray();
                polygons = ManifoldNative.manifold_alloc_polygons();
                fixed (IntPtr* pHandles = handles)
                {
                    ManifoldNative.manifold_polygons(polygons, pHandles, (nuint)handles.Length);
                }

                extruded = ManifoldNative.manifold_alloc_manifold();
                ManifoldNative.manifold_extrude(extruded, polygons, height, 0, 0, 1, 1);

                var status = ManifoldNative.manifold_status(extruded);
                if (status != ManifoldStatus.NoError) return ManifoldErrors.OperationFailed(status);
                if (ManifoldNative.manifold_num_tri(extruded) == 0) return ManifoldErrors.EmptyResult;

                // Extrude always starts at z = 0; shift it onto the requested range.
                placed = ManifoldNative.manifold_alloc_manifold();
                ManifoldNative.manifold_translate(placed, extruded, 0, 0, zOffset);

                return Extract(placed, metadata);
            }
            finally
            {
                if (placed != IntPtr.Zero) ManifoldNative.manifold_delete_manifold(placed);
                if (extruded != IntPtr.Zero) ManifoldNative.manifold_delete_manifold(extruded);
                if (polygons != IntPtr.Zero) ManifoldNative.manifold_delete_polygons(polygons);
                foreach (var polygon in simplePolygons) ManifoldNative.manifold_delete_simple_polygon(polygon);
            }
        });
    }

    /// <summary>
    /// Splits a solid into its disjoint parts. Returns a single-element list when there is only
    /// one, so callers do not have to special-case that.
    /// </summary>
    public static Result<IReadOnlyList<IMesh>> Decompose(IMesh mesh, MeshMetadata metadata)
    {
        return Guarded<IReadOnlyList<IMesh>>(() =>
        {
            var operand = ToManifold(mesh);
            if (operand.IsFailure) return operand.Error;

            var handle = operand.Value.Handle;
            var vector = IntPtr.Zero;
            try
            {
                vector = ManifoldNative.manifold_alloc_manifold_vec();
                ManifoldNative.manifold_decompose(vector, handle);

                int count = (int)ManifoldNative.manifold_manifold_vec_length(vector);
                var parts = new List<IMesh>(count);

                for (int i = 0; i < count; i++)
                {
                    var part = ManifoldNative.manifold_alloc_manifold();
                    try
                    {
                        ManifoldNative.manifold_manifold_vec_get(part, vector, (nuint)i);
                        var extracted = Extract(part, metadata);
                        if (extracted.IsFailure) return extracted.Error;
                        parts.Add(extracted.Value);
                    }
                    finally
                    {
                        ManifoldNative.manifold_delete_manifold(part);
                    }
                }

                return Result.Success<IReadOnlyList<IMesh>>(parts);
            }
            finally
            {
                if (vector != IntPtr.Zero) ManifoldNative.manifold_delete_manifold_vec(vector);
                ManifoldNative.manifold_delete_manifold(handle);
            }
        });
    }

    private static Result<ManifoldOutcome> RunBoolean(
        IMesh left, IMesh right, MeshMetadata metadata, BooleanOperation operation)
    {
        return Guarded(() =>
        {
            var leftOperand = ToManifold(left);
            if (leftOperand.IsFailure) return leftOperand.Error;

            var rightOperand = ToManifold(right);
            if (rightOperand.IsFailure)
            {
                ManifoldNative.manifold_delete_manifold(leftOperand.Value.Handle);
                return rightOperand.Error;
            }

            var provenance = leftOperand.Value.Merged || rightOperand.Value.Merged
                ? ManifoldProvenance.NativeAfterMergingOperands
                : ManifoldProvenance.Native;

            var result = IntPtr.Zero;
            try
            {
                result = ManifoldNative.manifold_alloc_manifold();
                operation(result, leftOperand.Value.Handle, rightOperand.Value.Handle);

                var status = ManifoldNative.manifold_status(result);
                if (status != ManifoldStatus.NoError) return ManifoldErrors.OperationFailed(status);

                var mesh = Extract(result, metadata);
                return mesh.IsFailure
                    ? Result.Failure<ManifoldOutcome>(mesh.Error)
                    : Result.Success(new ManifoldOutcome(mesh.Value, provenance));
            }
            finally
            {
                if (result != IntPtr.Zero) ManifoldNative.manifold_delete_manifold(result);
                ManifoldNative.manifold_delete_manifold(rightOperand.Value.Handle);
                ManifoldNative.manifold_delete_manifold(leftOperand.Value.Handle);
            }
        });
    }

    /// <summary>A live native solid, and whether its geometry had to be welded to build it.</summary>
    private readonly record struct Operand(IntPtr Handle, bool Merged);

    private static Result<Operand> ToManifold(IMesh mesh)
    {
        // An empty operand cannot be represented here: manifold_alloc_manifold only allocates
        // storage, it does not construct a solid, so handing the raw block on would put
        // uninitialised memory through a native boolean and then a native destructor.
        if (mesh.VertexCount == 0 || mesh.TriangleCount == 0)
        {
            return ManifoldErrors.EmptyOperand(mesh.Metadata.Name);
        }

        var vertProps = new double[mesh.VertexCount * 3];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var v = mesh.Vertices[i];
            vertProps[i * 3] = v.X;
            vertProps[i * 3 + 1] = v.Y;
            vertProps[i * 3 + 2] = v.Z;
        }

        var triVerts = new ulong[mesh.Triangles.Length];
        for (int i = 0; i < mesh.Triangles.Length; i++)
        {
            triVerts[i] = (ulong)mesh.Triangles[i];
        }

        fixed (double* pVerts = vertProps)
        fixed (ulong* pTris = triVerts)
        {
            var meshGl = ManifoldNative.manifold_alloc_meshgl64();
            try
            {
                ManifoldNative.manifold_meshgl64(
                    meshGl, pVerts, (nuint)mesh.VertexCount, 3, pTris, (nuint)mesh.TriangleCount);

                var solid = ManifoldNative.manifold_alloc_manifold();
                ManifoldNative.manifold_of_meshgl64(solid, meshGl);

                var status = ManifoldNative.manifold_status(solid);
                bool merged = false;

                if (status != ManifoldStatus.NoError)
                {
                    // Not a 2-manifold as supplied. Manifold's own merge welds near-coincident
                    // vertices, which closes the common case of a surface exported with
                    // unshared vertices - an STL has no index buffer at all, so every mesh off
                    // disk arrives that way. It also alters the geometry, so the caller is told.
                    ManifoldNative.manifold_delete_manifold(solid);

                    var mergedMeshGl = ManifoldNative.manifold_alloc_meshgl64();
                    try
                    {
                        ManifoldNative.manifold_meshgl64_merge(mergedMeshGl, meshGl);
                        solid = ManifoldNative.manifold_alloc_manifold();
                        ManifoldNative.manifold_of_meshgl64(solid, mergedMeshGl);
                        status = ManifoldNative.manifold_status(solid);
                        merged = status == ManifoldStatus.NoError;
                    }
                    finally
                    {
                        ManifoldNative.manifold_delete_meshgl64(mergedMeshGl);
                    }
                }

                if (status != ManifoldStatus.NoError)
                {
                    ManifoldNative.manifold_delete_manifold(solid);
                    return ManifoldErrors.InvalidMesh(mesh.Metadata.Name, status);
                }

                return Result.Success(new Operand(solid, merged));
            }
            finally
            {
                ManifoldNative.manifold_delete_meshgl64(meshGl);
            }
        }
    }

    private static Result<IMesh> Extract(IntPtr solid, MeshMetadata metadata)
    {
        if (ManifoldNative.manifold_is_empty(solid) != 0)
        {
            return Result.Success<IMesh>(new ManifoldMesh(Array.Empty<Vector3>(), Array.Empty<int>(), metadata));
        }

        var meshGl = ManifoldNative.manifold_alloc_meshgl64();
        try
        {
            ManifoldNative.manifold_get_meshgl64(meshGl, solid);

            int vertexCount = (int)ManifoldNative.manifold_meshgl64_num_vert(meshGl);
            int triangleCount = (int)ManifoldNative.manifold_meshgl64_num_tri(meshGl);
            int vertPropsLength = (int)ManifoldNative.manifold_meshgl64_vert_properties_length(meshGl);
            int triVertsLength = (int)ManifoldNative.manifold_meshgl64_tri_length(meshGl);

            if (vertexCount == 0 || triangleCount == 0)
            {
                return Result.Success<IMesh>(new ManifoldMesh(Array.Empty<Vector3>(), Array.Empty<int>(), metadata));
            }

            // The position is only the first three of however many properties a vertex carries.
            // The stride is read from the library rather than assumed to be three, so extra
            // properties cannot be silently read as coordinates.
            int stride = (int)ManifoldNative.manifold_meshgl64_num_prop(meshGl);
            if (stride < 3)
            {
                return ManifoldErrors.UnexpectedLayout(
                    $"vertices carry {stride} properties, too few to hold a position");
            }

            // Cross-check the stride against the buffer the library will fill, so a disagreement
            // is reported rather than read out of bounds.
            if (vertPropsLength != vertexCount * stride)
            {
                return ManifoldErrors.UnexpectedLayout(
                    $"vertex property buffer of {vertPropsLength} does not hold {vertexCount} vertices of {stride} properties");
            }

            if (triVertsLength != triangleCount * 3)
            {
                return ManifoldErrors.UnexpectedLayout(
                    $"triangle index buffer of {triVertsLength} does not describe {triangleCount} triangles");
            }

            var vertProps = new double[vertPropsLength];
            var triVerts = new ulong[triVertsLength];

            fixed (double* pVerts = vertProps)
            fixed (ulong* pTris = triVerts)
            {
                ManifoldNative.manifold_meshgl64_vert_properties((IntPtr)pVerts, meshGl);
                ManifoldNative.manifold_meshgl64_tri_verts((IntPtr)pTris, meshGl);
            }

            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(
                    (float)vertProps[i * stride],
                    (float)vertProps[i * stride + 1],
                    (float)vertProps[i * stride + 2]);
            }

            var triangles = new int[triVertsLength];
            for (int i = 0; i < triVertsLength; i++)
            {
                triangles[i] = (int)triVerts[i];
            }

            return Result.Success<IMesh>(new ManifoldMesh(vertices, triangles, metadata));
        }
        finally
        {
            ManifoldNative.manifold_delete_meshgl64(meshGl);
        }
    }

    /// <summary>
    /// Runs a native operation, turning the exceptions a P/Invoke can raise into failures.
    /// </summary>
    private static Result<T> Guarded<T>(Func<Result<T>> operation)
    {
        if (!ManifoldNative.IsAvailable) return ManifoldErrors.Unavailable;

        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            // The library is absent or unusable on this machine - ordinary on a platform whose
            // binaries do not ship.
            return ManifoldErrors.Unusable(exception);
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or MarshalDirectiveException)
        {
            // The library loaded but does not have the shape this binding expects: a wrong export
            // name, or a signature that cannot be marshalled. That is a defect here or a version
            // mismatch, not a platform limitation, and it is reported as its own failure so it
            // cannot be mistaken for one.
            return ManifoldErrors.BindingMismatch(exception);
        }
    }
}

/// <summary>Failures the native kernel can report.</summary>
internal static class ManifoldErrors
{
    public static readonly Error Unavailable = new(
        "Manifold.Unavailable",
        "The native Manifold library is not available for this platform or deployment. " +
        "Place manifoldc alongside the assembly, or use the MeshLib engine instead.");

    public static Error Unusable(Exception exception) => new(
        "Manifold.Unavailable",
        $"The native Manifold library could not be loaded: {exception.Message}");

    /// <summary>
    /// The library is present but does not match this binding - likely a version other than the
    /// Manifold 3.x these entry points were written against.
    /// </summary>
    public static Error BindingMismatch(Exception exception) => new(
        "Manifold.BindingMismatch",
        "The native Manifold library does not match this binding - it is likely a version other " +
        $"than the Manifold 3.x expected: {exception.Message}");

    public static Error EmptyOperand(string name) => new(
        "Manifold.EmptyOperand",
        $"Mesh '{name}' has no geometry, so it cannot be used as an operand.");

    public static Error InvalidMesh(string name, ManifoldStatus status) => new(
        "Manifold.InvalidMesh",
        $"Mesh '{name}' is not a valid manifold: {status}");

    public static Error OperationFailed(ManifoldStatus status) => new(
        "Manifold.OperationFailed",
        $"The operation failed with status: {status}");

    public static Error UnexpectedLayout(string detail) => new(
        "Manifold.UnexpectedLayout",
        $"The native library returned a mesh in an unexpected layout: {detail}.");

    public static readonly Error EmptyResult =
        new("Manifold.EmptyResult", "The operation produced an empty manifold.");
}
