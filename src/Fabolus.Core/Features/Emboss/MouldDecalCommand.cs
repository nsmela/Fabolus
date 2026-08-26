using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Decal;

public sealed record MouldDecalCommand(IReadOnlyList<TextDecal> Decals) : IMeshCommand
{
    public int Priority => CommandPriority.MouldTextEmboss;

    public string Describe() => $"Mould decals ({DecalSummary.Of(Decals)})";

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        var source = GlyphOutlineSourceProvider.Default;
        if (source is null)
        {
            return DecalErrors.MissingOutlineSource;
        }
        var tool = new GenerateDecals(source);
        return tool.Execute(engine, mesh, Decals);
    }
}
