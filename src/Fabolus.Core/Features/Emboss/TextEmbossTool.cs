using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Legacy alias for <see cref="GenerateDecals"/>.
/// </summary>
public sealed class TextEmbossTool
{
    private readonly GenerateDecals _generator;

    public TextEmbossTool(IGlyphOutlineSource outlineSource)
    {
        _generator = new GenerateDecals(outlineSource);
    }

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh target, IReadOnlyList<TextDecal> decals, List<string>? warnings = null) =>
        _generator.Execute(engine, target, decals, warnings);

    public Result<IMesh> ApplySingle(IGeometryEngine engine, IMesh target, TextDecal decal, List<string>? warnings = null) =>
        _generator.ExecuteSingle(engine, target, decal, warnings);
}
