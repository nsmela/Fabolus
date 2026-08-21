using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Executes 3D text solid generation, surface projection, and Boolean union/subtraction.
/// </summary>
public sealed class TextEmbossTool
{
    private readonly IGlyphOutlineSource _outlineSource;

    public TextEmbossTool(IGlyphOutlineSource outlineSource)
    {
        _outlineSource = outlineSource;
    }

    /// <summary>
    /// Applies the text decal to the target mesh.
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh target, TextDecal decal, List<string>? warnings = null)
    {
        if (target == null)
            return new Error("TextEmboss.NullTarget", "Target mesh cannot be null.");

        if (string.IsNullOrWhiteSpace(decal.Text))
            return new Error("TextEmboss.EmptyText", "Text label cannot be empty.");

        var outlines = _outlineSource.GetOutlines(decal.Text, decal.Font, decal.CapHeight, decal.Tracking);
        if (outlines.Count == 0)
            return new Error("TextEmboss.NoOutlines", "Could not generate glyph outlines for the specified text.");

        if (decal.Mirror)
            outlines = outlines.MirrorX();

        var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);

        float sink = decal.Operation == EmbossOperation.Emboss ? -0.25f : -decal.Depth;
        float overshoot = 0.5f;
        float maxEdge = decal.ProjectOntoSurface ? Math.Max(0.5f, decal.CapHeight / 6.0f) : 0f;

        var prismResult = engine.Generators.BuildTextPrism(outlines, frame, decal.Depth, sink, overshoot, maxEdge);
        if (prismResult.IsFailure)
            return prismResult.Error;

        var prismMesh = prismResult.Value;

        if (decal.ProjectOntoSurface)
        {
            var projectResult = engine.Generators.ProjectTextPrism(target, frame, prismMesh, warnings);
            if (projectResult.IsFailure)
                return projectResult.Error;

            prismMesh = projectResult.Value;
        }

        var booleanResult = decal.Operation == EmbossOperation.Emboss
            ? engine.Booleans.Union(target, prismMesh)
            : engine.Booleans.Subtract(target, prismMesh);

        if (booleanResult.IsFailure)
            return new Error("TextEmboss.BooleanFailed", $"Boolean operation failed: {booleanResult.Error.Description}");

        var finalMesh = booleanResult.Value;

        var topologyResult = engine.Evaluators.ValidateTopology(finalMesh);
        if (topologyResult.IsSuccess)
        {
            var topo = topologyResult.Value;
            if (!topo.IsManifold || topo.HasCorruptTopology)
            {
                return new Error("TextEmboss.NonManifold", "Boolean produced a non-manifold mesh. Try a larger cap height or less depth.");
            }
        }

        return Result.Success(finalMesh);
    }
}
