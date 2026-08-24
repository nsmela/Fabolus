using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Executes 3D text solid generation, surface contouring, and Boolean union/subtraction.
/// </summary>
public sealed class TextEmbossTool
{
    private readonly IGlyphOutlineSource _outlineSource;

    public TextEmbossTool(IGlyphOutlineSource outlineSource)
    {
        _outlineSource = outlineSource;
    }

    /// <summary>
    /// Applies a collection of text decals to the target mesh in sequence.
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh target, IReadOnlyList<TextDecal> decals, List<string>? warnings = null)
    {
        if (target == null)
            return new Error("TextEmboss.NullTarget", "Target mesh cannot be null.");

        if (decals == null || decals.Count == 0)
            return Result.Success(target);

        var currentMesh = target;
        foreach (var decal in decals)
        {
            var result = ApplySingle(engine, currentMesh, decal, warnings);
            if (result.IsFailure) return result;
            currentMesh = result.Value;
        }

        return Result.Success(currentMesh);
    }

    /// <summary>
    /// Applies a single text decal to the target mesh.
    /// </summary>
    public Result<IMesh> ApplySingle(IGeometryEngine engine, IMesh target, TextDecal decal, List<string>? warnings = null)
    {
        if (target == null)
            return new Error("TextEmboss.NullTarget", "Target mesh cannot be null.");

        if (string.IsNullOrWhiteSpace(decal.Text))
            return new Error("TextEmboss.EmptyText", "Text label cannot be empty.");

        var outlines = _outlineSource.GetOutlines(decal.Text, decal.Font, decal.CapHeight, decal.Tracking);
        if (outlines.Count == 0)
            return new Error("TextEmboss.NoOutlines", "Could not generate glyph outlines for the specified text.");

        var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);

        float sink = decal.Operation == EmbossOperation.Emboss ? -0.25f : -decal.Depth;
        float overshoot = decal.Operation == EmbossOperation.Emboss ? 0.0f : 0.5f;
        float maxEdge = Math.Max(0.4f, decal.CapHeight / 8.0f);
        IMesh? surfaceTarget = target;

        var prismResult = engine.Generators.BuildTextPrism(outlines, frame, decal.Depth, sink, overshoot, maxEdge, surfaceTarget);
        if (prismResult.IsFailure)
            return prismResult.Error;

        var prismMesh = prismResult.Value;

        var booleanResult = decal.Operation == EmbossOperation.Emboss
            ? engine.Booleans.Union(target, prismMesh)
            : engine.Booleans.Subtract(target, prismMesh);

        if (booleanResult.IsFailure)
            return new Error("TextEmboss.BooleanFailed", $"Boolean operation failed: {booleanResult.Error.Description}");

        return ValidateAndReturn(engine, booleanResult.Value);
    }

    private static Result<IMesh> ValidateAndReturn(IGeometryEngine engine, IMesh mesh)
    {
        var topologyResult = engine.Evaluators.ValidateTopology(mesh);
        if (topologyResult.IsSuccess)
        {
            var topo = topologyResult.Value;
            if (!topo.IsManifold || topo.HasCorruptTopology)
            {
                return new Error("TextEmboss.NonManifold", "Boolean produced a non-manifold mesh. Try adjusting placement or depth.");
            }
        }
        return Result.Success(mesh);
    }
}
