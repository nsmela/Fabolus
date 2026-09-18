using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Executes 3D text solid generation, surface contouring, and Boolean union/subtraction for decals.
/// </summary>
public sealed class GenerateDecals
{
    private const float EmbossSinkOffset = -0.25f;
    private const float EmbossOvershootOffset = 0.0f;
    private const float EngraveOvershootOffset = 0.5f;
    private const float MinMaxEdgeLength = 0.4f;
    private const float CapHeightToEdgeLengthDivisor = 8.0f;

    private readonly IGlyphOutlineSource _outlineSource;

    public GenerateDecals(IGlyphOutlineSource outlineSource)
    {
        _outlineSource = outlineSource;
    }

    /// <summary>
    /// Applies a collection of text decals to the target mesh in sequence.
    /// </summary>
    public Result<IMesh> Execute(IGeometryEngine engine, IMesh target, IReadOnlyList<TextDecal> decals, List<string>? warnings = null)
    {
        if (target is null)
            return MeshErrors.NullSource;

        if (decals is null || decals.Count == 0)
            return DecalErrors.NoDecalsProvided;

        var currentMesh = target;
        foreach (var decal in decals)
        {
            var result = ExecuteSingle(engine, currentMesh, decal, warnings);
            if (result.IsFailure) return result;
            currentMesh = result.Value;
        }

        return Result.Success(currentMesh);
    }

    /// <summary>
    /// Asynchronously applies a collection of text decals to the target mesh.
    /// </summary>
    public Task<Result<IMesh>> ExecuteAsync(IGeometryEngine engine, IMesh target, IReadOnlyList<TextDecal> decals, List<string>? warnings = null) =>
        Task.Run(() => Execute(engine, target, decals, warnings));

    /// <summary>
    /// Applies a single text decal to the target mesh.
    /// </summary>
    public Result<IMesh> ExecuteSingle(IGeometryEngine engine, IMesh target, TextDecal decal, List<string>? warnings = null)
    {
        if (target is null)
            return MeshErrors.NullSource;

        if (decal is null)
            return DecalErrors.NoDecalsProvided;

        if (string.IsNullOrWhiteSpace(decal.Text))
            return DecalErrors.EmptyOutlines;

        var outlineResult = _outlineSource.GetOutlines(decal.Text, decal.Font, decal.CapHeight, decal.Tracking);
        if (outlineResult.IsFailure)
            return outlineResult.Error;

        var outlines = outlineResult.Value;
        if (outlines.Count == 0)
            return DecalErrors.EmptyOutlines;

        var frame = DecalFrame.FromHit(
            new System.Numerics.Vector3((float)decal.Anchor.X, (float)decal.Anchor.Y, (float)decal.Anchor.Z), 
            new System.Numerics.Vector3((float)decal.AnchorNormal.X, (float)decal.AnchorNormal.Y, (float)decal.AnchorNormal.Z), 
            decal.RotationDeg);

        float sink = decal.Operation == EmbossOperation.Emboss ? EmbossSinkOffset : -decal.Depth;
        float overshoot = decal.Operation == EmbossOperation.Emboss ? EmbossOvershootOffset : EngraveOvershootOffset;
        float maxEdge = Math.Max(MinMaxEdgeLength, decal.CapHeight / CapHeightToEdgeLengthDivisor);
        IMesh? surfaceTarget = target;

        var spec = new GeometryEngine.Core.Geometry.DecalPrismSpec(
            [.. outlines],
            new GeometryEngine.Core.Geometry.SurfaceFrame(
                new GeometryEngine.Core.Geometry.Primitives.Vec3(frame.Origin.X, frame.Origin.Y, frame.Origin.Z),
                new GeometryEngine.Core.Geometry.Primitives.Vec3(frame.U.X, frame.U.Y, frame.U.Z),
                new GeometryEngine.Core.Geometry.Primitives.Vec3(frame.V.X, frame.V.Y, frame.V.Z),
                new GeometryEngine.Core.Geometry.Primitives.Vec3(frame.N.X, frame.N.Y, frame.N.Z)
            ),
            Depth: decal.Depth,
            Sink: sink,
            Overshoot: overshoot,
            MaxEdgeLength: maxEdge,
            SurfaceIndex: surfaceTarget is null ? BasicResults.Maybe<GeometryEngine.Core.Geometry.ISpatialIndex>.None() : BasicResults.Maybe<GeometryEngine.Core.Geometry.ISpatialIndex>.Some(engine.Spatial.BuildIndex(surfaceTarget).Value)
        );
        var prismResult = engine.Decals.BuildPrism(spec);
        if (prismResult.IsFailure)
            return prismResult.Error;

        var prismMesh = prismResult.Value;

        var booleanResult = decal.Operation == EmbossOperation.Emboss
            ? engine.Booleans.Union(target, prismMesh)
            : engine.Booleans.Subtract(target, prismMesh);

        if (booleanResult.IsFailure)
            return new Error("Decal.BooleanFailed", $"Boolean operation failed: {booleanResult.Error.Description}");

        return ValidateAndReturn(engine, booleanResult.Value);
    }

    private static Result<IMesh> ValidateAndReturn(IGeometryEngine engine, IMesh mesh)
    {
        var topologyResult = engine.Evaluators.ValidateTopology(mesh);
        if (topologyResult.IsSuccess)
        {
            var topo = topologyResult.Value;
            if (topo.HasCorruptTopology)
            {
                return MeshErrors.CorruptTopology;
            }
            if (!topo.IsManifold)
            {
                return new Error("Decal.NonManifold", "Boolean operation produced a non-manifold mesh. Try adjusting placement or depth.");
            }
        }
        return Result.Success(mesh);
    }
}
