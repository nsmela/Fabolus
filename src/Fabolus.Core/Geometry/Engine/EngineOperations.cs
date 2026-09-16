using System.Collections.Immutable;
using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Overhangs;
using Fabolus.Core.Geometry.Metadata;
using GE = GeometryEngine.Core.Geometry;
using GEC = GeometryEngine.Core.Common;
using GEP = GeometryEngine.Core.Geometry.Primitives;

namespace Fabolus.Core.Geometry.Engine;

internal sealed class EngineBooleans(GE.IGeometryEngine engine) : IBooleans
{
    // MeshLib's operation names leaked into mesh names, and features read them back, so they stay.
    public Result<IMesh> Union(IMesh meshA, IMesh meshB) => Run(meshA, meshB, "Union", engine.Booleans.Union);

    public Result<IMesh> Subtract(IMesh meshA, IMesh meshB) => Run(meshA, meshB, "DifferenceAB", engine.Booleans.Subtract);

    public Result<IMesh> Intersect(IMesh meshA, IMesh meshB) => Run(meshA, meshB, "Intersection", engine.Booleans.Intersect);

    private static Result<IMesh> Run(
        IMesh meshA, IMesh meshB, string operation, Func<GE.IMesh, GE.IMesh, GEC.Result<GE.IMesh>> boolean)
    {
        var a = meshA.ToEngine();
        if (a.IsFailure) return a.Error;
        var b = meshB.ToEngine();
        if (b.IsFailure) return b.Error;

        var result = boolean(a.Value, b.Value);
        if (result.IsFailure) return EngineErrors.Failed("Boolean", result.Error);

        // The library records which kernel produced the result; keep that visible to a caller
        // deciding whether it is safe to print.
        var metadata = EngineConversions.NewMetadata(
            $"{meshA.Metadata.Name} {operation} {meshB.Metadata.Name}",
            $"BooleanOperation ({result.Value.Metadata.CreatedBy})");

        return Result.Success(result.Value.ToFabolus(metadata));
    }
}

internal sealed class EngineTransforms(GE.IGeometryEngine engine) : IGeometryTransforms
{
    public Result<IMesh> Translate(IMesh source, double dx, double dy, double dz)
    {
        var metadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Translated ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Translate({dx}, {dy}, {dz})"));

        return Apply(source, metadata, mesh => engine.Transforms.Translate(mesh, new GEP.Vec3(dx, dy, dz)));
    }

    public Result<IMesh> Scale(IMesh source, double factor)
    {
        if (!(factor > 0)) return EngineErrors.InvalidScale;

        var metadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Scaled ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Scale({factor}, {factor}, {factor})"));

        return Apply(source, metadata, mesh => engine.Transforms.Scale(mesh, new GEP.Vec3(factor, factor, factor)));
    }

    public Result<IMesh> Rotate(IMesh source, Quaternion q)
    {
        // Normalised so a slightly-off quaternion rotates without scaling, then taken apart into
        // the axis and angle the library rotates by.
        double w = q.W, x = q.X, y = q.Y, z = q.Z;
        var norm = Math.Sqrt(w * w + x * x + y * y + z * z);
        if (norm < 1e-12) return Result.Success(Copy(source, source.Metadata));

        w /= norm; x /= norm; y /= norm; z /= norm;
        var sine = Math.Sqrt(x * x + y * y + z * z);
        var axis = GEP.Direction.From(new GEP.Vec3(x, y, z));
        if (sine < 1e-12 || axis.HasNoValue) return Result.Success(Copy(source, source.Metadata));

        var angle = 2 * Math.Atan2(sine, w);
        return Apply(source, source.Metadata, mesh => engine.Transforms.Rotate(mesh, axis.Value, angle));
    }

    private static Result<IMesh> Apply(IMesh source, MeshMetadata metadata, Func<GE.IMesh, GEC.Result<GE.IMesh>> transform)
    {
        // The library refuses to transform nothing; Fabolus has always handed an empty mesh back.
        if (source.IsEmpty) return Result.Success(Copy(source, metadata));

        var mesh = source.ToEngine();
        if (mesh.IsFailure) return mesh.Error;

        var result = transform(mesh.Value);
        return result.IsSuccess
            ? Result.Success(result.Value.ToFabolus(metadata))
            : EngineErrors.Failed("Transform", result.Error);
    }

    private static IMesh Copy(IMesh source, MeshMetadata metadata) =>
        new EngineMesh((Vector3[])source.Vertices.Clone(), (int[])source.Triangles.Clone(), metadata);
}

internal sealed class EngineModifiers(GE.IGeometryEngine engine) : IGeometryModifiers
{
    public Result<IMesh> Offset(IMesh input, float offsetDistance, float cellSize = 0f) =>
        Run(input, "Offset", mesh => engine.Modifiers.Offset(mesh, offsetDistance, cellSize), input.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Offset ({input.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Offset({offsetDistance})")));

    public Result<IMesh> OffsetDouble(IMesh input, float offsetDistance, int iterations = 1, float cellSize = 0f)
    {
        if (iterations < 1) return Result.Success(input);

        return Run(input, "OffsetDouble", mesh => engine.Modifiers.DoubleOffset(mesh, offsetDistance, iterations, cellSize), input.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"DoubleOffset ({input.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"OffsetDouble({offsetDistance}, {iterations})")));
    }

    public Result<IMesh> Resize(IMesh mesh, int targetTriangleCount)
    {
        if (mesh.TriangleCount <= targetTriangleCount) return Result.Success(mesh);

        return Run(mesh, "Resize", m => engine.Modifiers.Decimate(m, Math.Max(4, targetTriangleCount)), mesh.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Resized ({mesh.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Resize({targetTriangleCount})")));
    }

    public Result<IMesh> Repair(IMesh input) =>
        Run(input, "Repair", engine.Modifiers.Repair, input.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Repaired ({input.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, "Repair")));

    public Result<IMesh> RepairSelfIntersections(IMesh input) =>
        Run(input, "RepairSelfIntersections", engine.Modifiers.RepairSelfIntersections, input.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Repaired SI ({input.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, "RepairSelfIntersections")));

    private static Result<IMesh> Run(IMesh input, string operation, Func<GE.IMesh, GEC.Result<GE.IMesh>> modify, MeshMetadata metadata)
    {
        var mesh = input.ToEngine();
        if (mesh.IsFailure) return mesh.Error;

        var result = modify(mesh.Value);
        return result.IsSuccess
            ? Result.Success(result.Value.ToFabolus(metadata))
            : EngineErrors.Failed(operation, result.Error);
    }
}

internal sealed class EngineGenerators(GE.IGeometryEngine engine) : IGeometryGenerators
{
    /// <summary>
    /// The surface a decal was last built against, prepared for querying.
    ///
    /// Preparing one costs far more than building a decal does, and the decal view builds prism
    /// after prism against a surface that does not change - a label being dragged across a model,
    /// a preset being hovered, several labels on one mesh. Holding the last one turns every call
    /// after the first into the query it should have been. The converted mesh rides along inside
    /// it, so that conversion stops being repeated too.
    ///
    /// Meshes here are immutable, so the reference identifies the surface. A superseded index is
    /// dropped rather than disposed: a build on another thread may still be reading it, and what
    /// it holds is managed memory that the collector reclaims once nothing is.
    /// </summary>
    private readonly object _surfaceLock = new();
    private IMesh? _surfaceMesh;
    private GE.ISpatialIndex? _surfaceIndex;

    private Result<GE.ISpatialIndex> SurfaceIndexFor(IMesh mesh)
    {
        lock (_surfaceLock)
        {
            if (ReferenceEquals(_surfaceMesh, mesh) && _surfaceIndex is not null)
            {
                return Result.Success(_surfaceIndex);
            }
        }

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var built = engine.Spatial.BuildIndex(converted.Value);
        if (built.IsFailure) return EngineErrors.Failed("Spatial", built.Error);

        lock (_surfaceLock)
        {
            _surfaceMesh = mesh;
            _surfaceIndex = built.Value;
            return Result.Success(built.Value);
        }
    }

    public Result PrepareDecalSurface(IMesh targetMesh)
    {
        if (targetMesh is null) return MeshErrors.NullSource;
        if (targetMesh.TriangleCount == 0) return Result.Success();

        var index = SurfaceIndexFor(targetMesh);
        return index.IsFailure ? index.Error : Result.Success();
    }

    public Result<IMesh> GenerateTube(TubeParameters parameters)
    {
        var spec = new GE.TubeSpec(
            [.. parameters.Path.Select(p => p.ToEngine())],
            [.. parameters.Radii.Select(r => (double)r)],
            parameters.Segments,
            parameters.Capped);

        var tube = engine.Generators.GenerateTube(spec);
        if (tube.IsFailure) return GeneratorError(tube.Error);

        return Result.Success(tube.Value.ToFabolus(EngineConversions.NewMetadata(
            "Generated Tube", $"GenerateTube(segments={parameters.Segments}, points={parameters.Path.Count})")));
    }

    public Result<IMesh> GenerateSphere(Vector3 center, double radius, int slices = 16)
    {
        var sphere = engine.Generators.GenerateSphere(center.ToEngine(), radius, slices);
        return sphere.IsSuccess
            ? Result.Success(sphere.Value.ToFabolus(EngineConversions.NewMetadata("Generated Mesh", "CreateMesh")))
            : GeneratorError(sphere.Error);
    }

    public IReadOnlyList<Vector3> Arc3d(float bendRadius, Vector3 startPoint, Vector3 startDirection, Vector3 endDirection, int segmentsCount)
    {
        var arc = engine.Generators.GenerateArc(bendRadius, startPoint.ToEngine(), startDirection.ToEngine(), endDirection.ToEngine(), segmentsCount);
        return arc.IsSuccess ? arc.Value.Select(p => p.ToFabolus()).ToList() : [startPoint];
    }

    public Result<IMesh> GenerateExtrudedPath(ExtrudedPathParameters parameters)
    {
        var surface = parameters.TargetMesh is null ? null : parameters.TargetMesh.ToEngine();
        if (surface is { IsFailure: true }) return surface.Error;

        var spec = new GE.DrapedPathSpec(
            [.. parameters.Path.Select(p => p.ToEngine())],
            parameters.Radius,
            Depth: parameters.ZMin,
            TopHeight: parameters.ZMax,
            Surface: surface is null ? GEC.Maybe<GE.IMesh>.None() : GEC.Maybe<GE.IMesh>.Some(surface.Value));

        var draped = engine.Generators.GenerateDrapedPath(spec);
        if (draped.IsFailure) return GeneratorError(draped.Error);

        return Result.Success(draped.Value.ToFabolus(EngineConversions.NewMetadata(
            "Painted Air Channel", $"GenerateExtrudedPath(radius={parameters.Radius}, points={parameters.Path.Count})")));
    }

    public Result<IReadOnlyList<Vector3>> ResampleOpenPath(IReadOnlyList<Vector3> path, float targetSpacing, int smoothingIterations = 2)
    {
        if (path is null || path.Count == 0) return EngineErrors.InvalidPath;

        var resampled = engine.Generators.ResampleOpenPath([.. path.Select(p => p.ToEngine())], targetSpacing, smoothingIterations);
        if (resampled.IsFailure) return GeneratorError(resampled.Error);

        // A path the library hands back unchanged is returned as the caller's own instance.
        return path.Count < 3 || targetSpacing <= 0
            ? Result<IReadOnlyList<Vector3>>.Success(path)
            : Result<IReadOnlyList<Vector3>>.Success(resampled.Value.Select(p => p.ToFabolus()).ToList());
    }

    public Result<IMesh> BuildTextPrism(IReadOnlyList<Polygon2D> outlines, DecalFrame frame, float depth, float sink, float overshoot, float maxEdgeLength = 0f, IMesh? targetMesh = null)
    {
        if (outlines is null || outlines.Count == 0) return DecalErrors.EmptyOutlines;

        GEC.Maybe<GE.ISpatialIndex> surface = GEC.Maybe<GE.ISpatialIndex>.None();
        if (targetMesh is not null && targetMesh.TriangleCount > 0)
        {
            var index = SurfaceIndexFor(targetMesh);
            if (index.IsFailure) return index.Error;
            surface = GEC.Maybe<GE.ISpatialIndex>.Some(index.Value);
        }

        var spec = new GE.DecalPrismSpec(
            [.. outlines.Select(o => o.ToEngine())],
            ToEngine(frame),
            depth,
            sink,
            overshoot,
            maxEdgeLength,
            SurfaceIndex: surface);

        var prism = engine.Decals.BuildPrism(spec);
        if (prism.IsFailure)
        {
            return prism.Error.Code == "Decals.TriangulationFailed" ? DecalErrors.TriangulationFailed : DecalErrors.EmptyOutlines;
        }

        return Result.Success(prism.Value.ToFabolus(EngineConversions.NewMetadata("Text Prism", "TextMeshBuilder.BuildPrism")));
    }

    public Result<IMesh> ProjectTextPrism(IMesh targetMesh, DecalFrame frame, IMesh prismMesh, List<string>? warnings = null)
    {
        if (targetMesh is null || prismMesh is null) return Result.Success(prismMesh!);

        var surface = SurfaceIndexFor(targetMesh);
        if (surface.IsFailure) return surface.Error;
        var prism = prismMesh.ToEngine();
        if (prism.IsFailure) return prism.Error;

        var projected = engine.Decals.ProjectPrism(surface.Value, ToEngine(frame), prism.Value);
        if (projected.IsFailure) return DecalErrors.RaycastFailed;

        if (projected.Value.ExtendsPastSurface) warnings?.Add("Label extends past the surface");
        if (projected.Value.SurfaceTooCurved) warnings?.Add("Surface too curved for this size");

        return Result.Success(projected.Value.Mesh.ToFabolus(prismMesh.Metadata));
    }

    private static GE.SurfaceFrame ToEngine(DecalFrame frame) =>
        new(frame.Origin.ToEngine(), frame.U.ToEngine(), frame.V.ToEngine(), frame.N.ToEngine());

    private static Error GeneratorError(GEC.Error error) => error.Code switch
    {
        "Generators.PathTooShort" or "Generators.DegeneratePath" or "Generators.NonFinitePath" => EngineErrors.InvalidPath,
        "Generators.RadiiMismatch" => EngineErrors.InvalidRadii,
        "Generators.NonPositiveRadius" => EngineErrors.InvalidRadius,
        "Generators.TooFewSegments" => EngineErrors.InvalidSegments,
        "Generators.TriangulationFailed" => EngineErrors.TriangulationFailed(error.Description),
        "Polygons.OffsetFailed" => EngineErrors.OffsetFailed(error.Description),
        _ => error.ToFabolus(),
    };
}

internal sealed class EngineEvaluators(GE.IGeometryEngine engine) : IGeometryEvaluators
{
    /// <summary>Fabolus's threshold for a triangle too small to be surface, in square millimetres.</summary>
    private const double DegenerateTriangleArea = 1e-6;

    /// <summary>Separated components below this volume, in cubic millimetres, are boolean debris rather than parts.</summary>
    private const double MinComponentVolume = 0.1;

    public Result<IReadOnlyList<Vector3>> ComputeVertexNormals(IMesh mesh)
    {
        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var normals = engine.Evaluators.ComputeVertexNormals(converted.Value);
        return normals.IsSuccess
            ? Result.Success<IReadOnlyList<Vector3>>(normals.Value.Select(n => n.ToFabolus()).ToList())
            : EngineErrors.Failed("Evaluator", normals.Error);
    }

    public Result<TopologyValidation> ValidateTopology(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (mesh.IsEmpty) return new TopologyValidation { IsWatertight = true, IsManifold = true };

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var topology = engine.Evaluators.ValidateTopology(converted.Value);
        if (topology.IsFailure) return EngineErrors.Failed("Evaluator", topology.Error);

        var selfIntersections = engine.Evaluators.CountSelfIntersections(converted.Value);

        return new TopologyValidation
        {
            HasCorruptTopology = false,
            IsWatertight = topology.Value.IsClosed,
            IsManifold = topology.Value.IsEdgeManifold,
            HasOrphanedVertices = topology.Value.UnreferencedVertexCount > 0,
            HasDegenerateTriangles = HasDegenerateTriangle(mesh),
            VertexCount = mesh.VertexCount,
            TriangleCount = mesh.TriangleCount,
            BoundaryEdgeCount = topology.Value.BoundaryEdgeCount,
            NonManifoldEdgeCount = topology.Value.NonManifoldEdgeCount,
            SelfIntersectionCount = selfIntersections.IsSuccess ? selfIntersections.Value : 0,
        };
    }

    public Result<MeshStatistics> GetStatistics(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (mesh.IsEmpty) return new MeshStatistics { VertexCount = mesh.VertexCount, TriangleCount = mesh.TriangleCount };

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var stats = engine.Evaluators.GetStatistics(converted.Value);
        var topology = engine.Evaluators.ValidateTopology(converted.Value);
        if (stats.IsFailure) return EngineErrors.Failed("Evaluator", stats.Error);
        if (topology.IsFailure) return EngineErrors.Failed("Evaluator", topology.Error);

        return new MeshStatistics
        {
            VertexCount = mesh.VertexCount,
            TriangleCount = mesh.TriangleCount,
            EdgeCount = topology.Value.EdgeCount,
            BoundaryEdgeCount = topology.Value.BoundaryEdgeCount,
            // Millilitres, and only for a surface that encloses something - as Fabolus has always reported.
            Volume = topology.Value.IsClosed ? stats.Value.Volume / 1000.0 : 0.0,
            SurfaceArea = stats.Value.SurfaceArea,
            MinX = stats.Value.BoundsMin.X,
            MinY = stats.Value.BoundsMin.Y,
            MinZ = stats.Value.BoundsMin.Z,
            MaxX = stats.Value.BoundsMax.X,
            MaxY = stats.Value.BoundsMax.Y,
            MaxZ = stats.Value.BoundsMax.Z,
        };
    }

    public Result<RenderData> GetRenderData(IMesh mesh)
    {
        var normals = ComputeVertexNormals(mesh);
        if (normals.IsFailure) return normals.Error;

        var positions = new double[mesh.VertexCount * 3];
        var flatNormals = new double[mesh.VertexCount * 3];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            positions[i * 3] = mesh.Vertices[i].X;
            positions[i * 3 + 1] = mesh.Vertices[i].Y;
            positions[i * 3 + 2] = mesh.Vertices[i].Z;

            flatNormals[i * 3] = normals.Value[i].X;
            flatNormals[i * 3 + 1] = normals.Value[i].Y;
            flatNormals[i * 3 + 2] = normals.Value[i].Z;
        }

        return new RenderData
        {
            Vertices = positions,
            Triangles = (int[])mesh.Triangles.Clone(),
            Normals = flatNormals,
        };
    }

    public Result<double[]> CalculateDeviationColors(IMesh current, IMesh original, double maxDeviation = 0.4)
    {
        if (current is null || original is null) return MeshErrors.NullSource;

        var target = original.ToEngine();
        if (target.IsFailure) return target.Error;

        var index = engine.Spatial.BuildIndex(target.Value);
        if (index.IsFailure) return EngineErrors.Failed("Evaluator", index.Error);

        using var spatial = index.Value;
        var distances = spatial.SignedDistances([.. current.Vertices.Select(v => v.ToEngine())]);

        var gradient = ColourGradient.SmoothingDeviation;
        var scale = Math.Max(maxDeviation, 0.001);
        var colours = new double[current.VertexCount * 3];

        for (int i = 0; i < distances.Length; i++)
        {
            // [-scale, scale] onto the gradient, so an unchanged surface lands mid-ramp.
            var t = Math.Clamp((distances[i] + scale) / (2.0 * scale), 0.0, 1.0);
            var colour = gradient.Sample((float)t);

            colours[i * 3] = colour.R;
            colours[i * 3 + 1] = colour.G;
            colours[i * 3 + 2] = colour.B;
        }

        return colours;
    }

    public Result<bool> HasMultipleComponents(IMesh mesh)
    {
        var components = Components(mesh);
        return components.IsSuccess ? components.Value.Length > 1 : components.Error;
    }

    public Result<IEnumerable<IMesh>> SeparateComponents(IMesh mesh)
    {
        var components = Components(mesh);
        if (components.IsFailure) return components.Error;
        if (components.Value.Length <= 1) return Result.Success<IEnumerable<IMesh>>([mesh]);

        var parts = new List<IMesh>(components.Value.Length);
        for (int i = 0; i < components.Value.Length; i++)
        {
            var volume = engine.Evaluators.GetStatistics(components.Value[i]);
            if (volume.IsFailure || volume.Value.Volume < MinComponentVolume) continue;

            parts.Add(components.Value[i].ToFabolus(new MeshMetadata().WithProperties(m =>
                m.Set(CoreKeys.Id, Guid.NewGuid())
                 .Set(CoreKeys.Name, $"{mesh.Metadata.Name} Component {i + 1}"))));
        }

        return Result.Success<IEnumerable<IMesh>>(parts);
    }

    public Result<RaycastHit> Raycast(IMesh mesh, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var direction = GEP.Direction.From(rayDirection.ToEngine());
        if (rayDirection.Length() < 1e-6f || direction.HasNoValue) return EngineErrors.InvalidDirection;
        if (mesh.IsEmpty) return MeshErrors.RaycastMiss;

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var index = engine.Spatial.BuildIndex(converted.Value);
        if (index.IsFailure) return MeshErrors.RaycastMiss;

        using var spatial = index.Value;
        var hit = spatial.Raycast(rayOrigin.ToEngine(), direction.Value);
        if (hit.HasNoValue) return MeshErrors.RaycastMiss;

        // Fabolus wants the normal facing back along the ray, whichever way the triangle winds.
        var normal = hit.Value.Normal;
        if (normal == GEP.Vec3.Zero) normal = -direction.Value.Vector;
        else if (normal.Dot(direction.Value.Vector) > 0) normal = -normal;

        return Result.Success(new RaycastHit(hit.Value.Point.ToFabolus(), normal.ToFabolus(), (float)hit.Value.Distance));
    }

    private Result<ImmutableArray<GE.IMesh>> Components(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (mesh.IsEmpty) return Result.Success(ImmutableArray<GE.IMesh>.Empty);

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var components = engine.Evaluators.SeparateComponents(converted.Value);
        return components.IsSuccess ? Result.Success(components.Value) : EngineErrors.Failed("Evaluator", components.Error);
    }

    private static bool HasDegenerateTriangle(IMesh mesh)
    {
        for (int i = 0; i + 2 < mesh.Triangles.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Triangles[i]];
            var b = mesh.Vertices[mesh.Triangles[i + 1]];
            var c = mesh.Vertices[mesh.Triangles[i + 2]];
            if (Vector3.Cross(b - a, c - a).Length() * 0.5 < DegenerateTriangleArea) return true;
        }

        return false;
    }
}

internal sealed class EnginePolygons(GE.IGeometryEngine engine) : IPolygonOperations
{
    public Result<Polygon2D> GetMeshShadow(IMesh mesh) => Project(mesh, engine.Polygons.ProjectOutline);

    public Result<Polygon2D> GetConvexHull(IMesh mesh) => Project(mesh, engine.Polygons.ProjectConvexHull);

    public Result<Polygon2D> OffsetPolygon(Polygon2D polygon, float distance)
    {
        var offset = engine.Polygons.Offset(polygon.ToEngine(), distance);
        return offset.IsSuccess ? offset.Value.ToFabolus() : EngineErrors.OffsetFailed(offset.Error.Description);
    }

    public Result<Polygon2D> BufferPath(IReadOnlyList<Vector2> path, float distance)
    {
        if (path is null || path.Count == 0) return EngineErrors.InvalidPath;

        var buffered = engine.Polygons.BufferPath([.. path.Select(p => p.ToEngine())], distance);
        return buffered.IsSuccess ? buffered.Value.ToFabolus() : EngineErrors.OffsetFailed(buffered.Error.Description);
    }

    public Result<Polygon2D> UnionPolygons(IReadOnlyList<Polygon2D> polygons)
    {
        if (polygons is null || polygons.Count == 0) return EngineErrors.UnionFailed("No polygons to union.");

        var union = engine.Polygons.Union([.. polygons.Select(p => p.ToEngine())]);
        return union.IsSuccess ? union.Value.ToFabolus() : EngineErrors.UnionFailed(union.Error.Description);
    }

    public Result<IMesh> ExtrudePolygon(Polygon2D polygon, float zMin, float zMax)
    {
        var extruded = engine.Polygons.Extrude(polygon.ToEngine(), zMin, zMax);
        return extruded.IsSuccess
            ? Result.Success(extruded.Value.ToFabolus(EngineConversions.NewMetadata("Extruded Mould", "ExtrudePolygon")))
            : EngineErrors.TriangulationFailed(extruded.Error.Description);
    }

    public Polygon2D MirrorX(Polygon2D polygon) => engine.Polygons.MirrorX(polygon.ToEngine()).ToFabolus();

    private static Result<Polygon2D> Project(IMesh mesh, Func<GE.IMesh, GEC.Result<GE.PlanarPolygon>> projection)
    {
        if (mesh is null) return EngineErrors.InvalidMesh;

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        var outline = projection(converted.Value);
        return outline.IsSuccess ? outline.Value.ToFabolus() : EngineErrors.HullFailed(outline.Error.Description);
    }
}
