using System.Text.Json.Serialization;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public sealed record MouldTextEmbossCommand(IReadOnlyList<TextDecal> Decals, [property: JsonIgnore] IGlyphOutlineSource? OutlineSource = null) : IMeshCommand
{
    [JsonConstructor]
    public MouldTextEmbossCommand(IReadOnlyList<TextDecal> Decals) : this(Decals, null)
    {
    }

    public int Priority => CommandPriority.MouldTextEmboss;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        var source = OutlineSource ?? GlyphOutlineSourceProvider.Default;
        if (source is null)
        {
            return new Error("MouldTextEmboss.MissingOutlineSource", "No glyph outline provider configured.");
        }
        var tool = new TextEmbossTool(source);
        return tool.Apply(engine, mesh, Decals);
    }
}
