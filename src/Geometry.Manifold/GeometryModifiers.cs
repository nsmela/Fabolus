using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal;
using GeometryManifold.Internal.Native;
using System.Numerics;

namespace GeometryManifold;

/// <summary>
/// Topology-changing operations. Offsetting goes through Manifold's level-set mesher, fed by a
/// signed distance field this project computes; decimation and repair have no Manifold equivalent
/// at all and are implemented here.
/// </summary>
internal sealed class GeometryModifiers : IGeometryModifiers
{
    /// <summary>
    /// Voxels across the longest side of the offset volume when the caller does not choose a cell
    /// size. High enough to keep a bolus recognisable, low enough that the field - which costs one
    /// closest-point query per sample - stays interactive.
    /// </summary>
    private const int DefaultOffsetResolution = 32;

    /// <summary>
    /// Ceiling on the total voxels sampled, whatever cell size the caller asks for. The smoothing
    /// feature passes its resolution straight through as a cell size, and on a 160mm bolus a 1mm
    /// cell is nearly five million samples - minutes of managed distance queries. MeshLib capped
    /// this the same way, through suggestVoxelSize's voxel budget.
    /// </summary>
    /// <remarks>
    /// Manifold samples a good deal finer than this nominal grid - its edgeLength is the target
    /// edge length of the output triangles, not the sample spacing - so the real query count runs
    /// well above the budget. Measured on sphere.stl offset by 2mm: a 5mm cell is accurate to
    /// three decimal places in under a second, where a 1.5mm cell takes eight for the same answer.
    /// </remarks>
    private const double MaxOffsetVoxels = 50_000;

    /// <summary>Padding on the sampled volume beyond the offset distance, so the new surface is never clipped by the box.</summary>
    private const float OffsetBoxMargin = 2.0f;

    private readonly GeometryEngine _engine;

    public GeometryModifiers(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IMesh> Offset(IMesh input, float offsetDistance, float cellSize = 0.0f)
    {
        try
        {
            var offset = OffsetOnce(input, offsetDistance, cellSize);
            if (offset.IsFailure) return offset.Error;

            var metadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Offset ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"Offset({offsetDistance})"));

            return Result.Success(offset.Value.WithMetadata(metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetFailed", ex.ToString());
        }
    }

    public Result<IMesh> OffsetDouble(IMesh input, float offsetDistance, int iterations = 1, float cellSize = 0.0f)
    {
        if (iterations < 1) return Result.Success(input);

        try
        {
            var current = input;
            for (int i = 0; i < iterations; i++)
            {
                // Out and back again: the round trip rounds off concave detail smaller than the
                // offset distance, which is what the smoothing feature wants from it.
                var grown = OffsetOnce(current, offsetDistance, cellSize);
                if (grown.IsFailure) break;

                var shrunk = OffsetOnce(grown.Value, -offsetDistance, cellSize);
                if (shrunk.IsFailure || shrunk.Value.IsEmpty) break;

                current = shrunk.Value;
            }

            var metadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"DoubleOffset ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"OffsetDouble({offsetDistance}, {iterations})"));

            return Result.Success(current.WithMetadata(metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetDoubleFailed", ex.ToString());
        }
    }

    /// <summary>
    /// Re-meshes the surface at a fixed distance from the input using Manifold's level-set
    /// mesher. This is the Manifold counterpart of MeshLib's voxel-based offsetMesh: sample a
    /// signed distance field on a grid, then extract the isosurface.
    /// </summary>
    private Result<IMesh> OffsetOnce(IMesh input, float offsetDistance, float cellSize)
    {
        if (input.TriangleCount == 0) return GeometryErrors.InvalidMesh;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in input.Vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }

        float padding = MathF.Abs(offsetDistance) + OffsetBoxMargin;
        min -= new Vector3(padding);
        max += new Vector3(padding);

        var size = max - min;
        float longestSide = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        float edgeLength = cellSize > 0 ? cellSize : longestSide / DefaultOffsetResolution;
        if (edgeLength <= 0) return GeometryErrors.InvalidMesh;

        // Coarsen rather than let a caller's cell size run the sample count away.
        double requestedVoxels = (double)size.X * size.Y * size.Z / Math.Pow(edgeLength, 3);
        if (requestedVoxels > MaxOffsetVoxels)
        {
            edgeLength *= (float)Math.Cbrt(requestedVoxels / MaxOffsetVoxels);
        }

        // The native shim runs the whole offset, callback included, in one call. Going through
        // the managed field instead costs a P/Invoke transition per sample, and this asks for
        // hundreds of thousands of them - the transitions, not the distances, are the expense.
        if (NativeDistanceField.IsAvailable)
        {
            var signMode = NativeDistanceField.ChooseSignMode(input);
            var native = NativeDistanceField.Offset(
                input, offsetDistance, min, max, edgeLength, signMode, input.Metadata);

            if (native.IsSuccess) return native;

            // Fall through to the managed path: the shim reports an absent Manifold or a
            // degenerate field as a value, and the managed field may still cope.
        }

        var bvh = new MeshBvh(input.Vertices, input.Triangles);
        if (bvh.IsEmpty) return GeometryErrors.InvalidMesh;

        return ManifoldKernel.LevelSet(
            bvh.SignedDistance, min, max, edgeLength, offsetDistance, input.Metadata);
    }

    public Result<IMesh> Resize(IMesh mesh, int targetTriangleCount)
    {
        try
        {
            if (mesh.TriangleCount <= targetTriangleCount)
                return Result.Success(mesh);

            var (vertices, triangles) = MeshDecimator.Decimate(mesh.Vertices, mesh.Triangles, targetTriangleCount);

            var metadata = mesh.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Resized ({mesh.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"Resize({targetTriangleCount})"));

            return Result.Success<IMesh>(new ManifoldMesh(vertices, triangles, metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.ResizeFailed", ex.ToString());
        }
    }

    public Result<IMesh> Repair(IMesh input)
    {
        try
        {
            // Welding merges coincident vertices and drops the triangles that collapse with them,
            // which covers the degeneracies and duplicate-vertex cracks MeshLib's
            // fixMeshDegeneracies handled. Compacting then clears the vertices left unreferenced.
            var (welded, weldedTriangles) = MeshExtensions.Weld(input.Vertices, input.Triangles);
            var (vertices, triangles) = MeshExtensions.Compact(welded, weldedTriangles);

            var metadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Repaired ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, "Repair"));

            return Result.Success<IMesh>(new ManifoldMesh(vertices, triangles, metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairFailed", ex.ToString());
        }
    }

    public Result<IMesh> RepairSelfIntersections(IMesh input)
    {
        try
        {
            var metadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Repaired SI ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, "RepairSelfIntersections"));

            // Manifold resolves self-intersections as a side effect of any boolean: unioning a
            // solid with itself re-cuts every crossing surface and returns a clean one. If the
            // mesh will not load as a manifold at all there is nothing to resolve, so the
            // geometry is handed back untouched rather than failing the pipeline.
            var resolved = ManifoldKernel.Union(input, input, metadata);

            return resolved.IsFailure
                ? Result.Success<IMesh>(new ManifoldMesh(
                    (Vector3[])input.Vertices.Clone(), (int[])input.Triangles.Clone(), metadata))
                : Result.Success(resolved.Value.Mesh);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairSelfIntersectionsFailed", ex.ToString());
        }
    }
}
