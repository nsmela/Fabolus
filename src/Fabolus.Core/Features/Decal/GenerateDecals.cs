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

    /// <summary>
    /// Refuses a decal result that is not a printable solid, and accepts one that is merely untidy.
    ///
    /// This used to reject anything with <c>HasCorruptTopology</c>, which counts slivers and
    /// coincident vertices alongside real defects. That check could never fire before the engine
    /// migration - the MeshLib evaluator hardcoded the flag to <c>false</c> - so it went live as a
    /// real measurement and started refusing meshes that were watertight, manifold and correctly
    /// wound. A saved mould carrying two degenerate triangles in twenty-five thousand was enough.
    ///
    /// What is actually checked is what would make the result unusable: a hole, which leaves the
    /// model with no well-defined inside for a slicer to fill, and a surface that is not a solid.
    /// Slivers and coincident vertices survive a boolean routinely, change nothing about
    /// printability, and are the mesh repair tool's business rather than grounds for refusing the
    /// operation the user asked for.
    /// </summary>
    private static Result<IMesh> ValidateAndReturn(IGeometryEngine engine, IMesh mesh)
    {
        var topologyResult = engine.Evaluators.ValidateTopology(mesh);

        // A mesh that cannot be measured is handed back rather than refused: failing to validate
        // is not evidence of a defect, and the caller can still repair or inspect it.
        return topologyResult.IsFailure
            ? Result.Success(mesh)
            : AcceptIfPrintable(mesh, topologyResult.Value);
    }

    /// <summary>
    /// The decision this gate makes, separated from measuring the mesh so it can be tested on its
    /// own - the defect it used to have was in the decision, not in the measurement.
    /// </summary>
    internal static Result<IMesh> AcceptIfPrintable(IMesh mesh, TopologyValidation topology)
    {
        // No well-defined inside for a slicer to fill: the decal has made the model unprintable.
        if (!topology.IsClosed)
        {
            return MeshErrors.NotWatertight;
        }

        // Two sheets of surface along one edge, a doubled face, or an inverted one - whatever the
        // boolean produced, it is not a solid.
        if (!topology.IsManifold)
        {
            return new Error("Decal.NonManifold", "Boolean operation produced a non-manifold mesh. Try adjusting placement or depth.");
        }

        // Deliberately not checked: HasRedundantGeometry, and the HasCorruptTopology flag that
        // folds it in with real defects. Slivers and coincident vertices survive a boolean
        // routinely - Manifold only re-meshes near the intersection, so anything untidy further
        // away passes straight through - and they change nothing about printability. They are the
        // repair tool's business, not grounds for refusing the operation the user asked for.
        return Result.Success(mesh);
    }
}
