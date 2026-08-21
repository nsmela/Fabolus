using System.Text.Json.Serialization;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public sealed record TextEmbossCommand(TextDecal Decal, [property: JsonIgnore] IGlyphOutlineSource? OutlineSource = null) : IMeshCommand
{
    [JsonConstructor]
    public TextEmbossCommand(TextDecal Decal) : this(Decal, null)
    {
    }

    public int Priority => Decal.Target == EmbossTarget.Mould ? CommandPriority.Mould + 5 : CommandPriority.TextEmboss;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        var source = OutlineSource ?? GlyphOutlineSourceProvider.Default;
        if (source is null)
        {
            return new Error("TextEmboss.MissingOutlineSource", "No glyph outline provider configured.");
        }
        var tool = new TextEmbossTool(source);
        return tool.Apply(engine, mesh, Decal);
    }
}
