using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Builds text prisms, contours them to the target surface, and joins them onto or cuts them into
/// the target with Boolean union/subtraction.
/// </summary>
public sealed class GenerateDecals
{
    private readonly IGlyphOutlineSource _outlineSource;

    public GenerateDecals(IGlyphOutlineSource outlineSource)
    {
        _outlineSource = outlineSource;
    }

    /// <summary>
    /// Applies a collection of text decals to the target mesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Boolean against the target costs in proportion to the target, which is tens of
    /// thousands of triangles, while a text prism is a few thousand. So rather than one Boolean
    /// against the target per decal, the prisms are merged first and the target is operated on
    /// once per run of decals that share an operation - usually once in all. Merging is a union
    /// of the small prisms, so overlapping labels still merge correctly. Union and subtraction
    /// are associative, so T - A - B = T - (A U B) and the result is the same set as before.
    /// Runs are kept in list order, so an engrave that follows an emboss still cuts into it.
    /// </para>
    /// <para>
    /// Every prism is contoured to the untouched target, through one spatial index, rather than
    /// to the mesh left by the decals before it. That is what the preview shows, and it only
    /// differs from the old sequential projection where two labels overlap.
    /// </para>
    /// </remarks>
    public Result<IMesh> Execute(IGeometryEngine engine, IMesh target, IReadOnlyList<TextDecal> decals, List<string>? warnings = null)
    {
        if (target is null)
            return MeshErrors.NullSource;

        if (decals is null || decals.Count == 0)
            return DecalErrors.NoDecalsProvided;

        var surfaceResult = DecalSurface.For(engine, target);
        if (surfaceResult.IsFailure)
            return surfaceResult.Error;

        var surface = surfaceResult.Value;

        var prisms = new List<(EmbossOperation Operation, IMesh Prism)>(decals.Count);
        foreach (var decal in decals)
        {
            if (decal is null)
                return DecalErrors.NoDecalsProvided;

            if (string.IsNullOrWhiteSpace(decal.Text))
                return DecalErrors.EmptyOutlines;

            var prism = surface.GetOrBuildPrism(engine, _outlineSource, DecalPrismRequest.ForApply(decal));
            if (prism.IsFailure)
                return prism.Error;

            prisms.Add((decal.Operation, prism.Value));
        }

        var current = target;
        for (int start = 0; start < prisms.Count;)
        {
            var operation = prisms[start].Operation;
            int end = start;
            while (end < prisms.Count && prisms[end].Operation == operation)
                end++;

            var run = new List<IMesh>(end - start);
            for (int i = start; i < end; i++)
                run.Add(prisms[i].Prism);

            var applied = ApplyRun(engine, current, operation, run);
            if (applied.IsFailure)
                return applied;

            current = applied.Value;
            start = end;
        }

        return ValidateAndReturn(engine, current);
    }

    /// <summary>
    /// Asynchronously applies a collection of text decals to the target mesh.
    /// </summary>
    public Task<Result<IMesh>> ExecuteAsync(IGeometryEngine engine, IMesh target, IReadOnlyList<TextDecal> decals, List<string>? warnings = null) =>
        Task.Run(() => Execute(engine, target, decals, warnings));

    /// <summary>
    /// Applies a single text decal to the target mesh.
    /// </summary>
    public Result<IMesh> ExecuteSingle(IGeometryEngine engine, IMesh target, TextDecal decal, List<string>? warnings = null) =>
        decal is null
            ? DecalErrors.NoDecalsProvided
            : Execute(engine, target, [decal], warnings);

    /// <summary>
    /// Joins or cuts one run of same-operation prisms with a single Boolean against the target.
    /// Falls back to one Boolean per prism - the old behaviour - if merging them, or the merged
    /// Boolean, fails, so a label that used to apply still does.
    /// </summary>
    private static Result<IMesh> ApplyRun(IGeometryEngine engine, IMesh target, EmbossOperation operation, IReadOnlyList<IMesh> prisms)
    {
        if (prisms.Count > 1)
        {
            var tool = MergePrisms(engine, prisms);
            if (tool.IsSuccess)
            {
                var merged = Combine(engine, target, tool.Value, operation);
                if (merged.IsSuccess)
                    return merged;
            }
        }

        var current = target;
        foreach (var prism in prisms)
        {
            var result = Combine(engine, current, prism, operation);
            if (result.IsFailure)
                return new Error("Decal.BooleanFailed", $"Boolean operation failed: {result.Error.Description}");

            current = result.Value;
        }

        return Result.Success(current);
    }

    /// <summary>
    /// Unions the prisms pairwise, level by level, so each Boolean joins two meshes of similar
    /// size rather than folding every prism into one ever-growing accumulator.
    /// </summary>
    private static Result<IMesh> MergePrisms(IGeometryEngine engine, IReadOnlyList<IMesh> prisms)
    {
        var level = new List<IMesh>(prisms);
        while (level.Count > 1)
        {
            var next = new List<IMesh>((level.Count + 1) / 2);
            for (int i = 0; i < level.Count; i += 2)
            {
                if (i + 1 == level.Count)
                {
                    next.Add(level[i]);
                    continue;
                }

                var union = engine.Booleans.Union(level[i], level[i + 1]);
                if (union.IsFailure)
                    return union.Error;

                next.Add(union.Value);
            }

            level = next;
        }

        return Result.Success(level[0]);
    }

    private static Result<IMesh> Combine(IGeometryEngine engine, IMesh target, IMesh tool, EmbossOperation operation) =>
        operation == EmbossOperation.Emboss
            ? engine.Booleans.Union(target, tool)
            : engine.Booleans.Subtract(target, tool);

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
