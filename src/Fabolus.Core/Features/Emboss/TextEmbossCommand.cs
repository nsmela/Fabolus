using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public sealed record TextEmbossCommand(TextDecal Decal, IGlyphOutlineSource OutlineSource) : IMeshCommand
{
    public int Priority => CommandPriority.TextEmboss;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        var tool = new TextEmbossTool(OutlineSource);
        return tool.Apply(engine, mesh, Decal);
    }
}
