using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public static class TextEmbossKeys
{
    public static readonly MetadataKey<IReadOnlyList<TextDecal>> TextDecals = new("TextDecals");
}

public static class TextEmbossMetadataExtensions
{
    public static Maybe<IReadOnlyList<TextDecal>> TextDecals(this MeshMetadata metadata)
    {
        var baseCmd = metadata.Commands.OfType<TextEmbossCommand>().FirstOrDefault();
        var mouldCmd = metadata.Commands.OfType<MouldTextEmbossCommand>().FirstOrDefault();

        if (baseCmd is not null || mouldCmd is not null)
        {
            var list = new List<TextDecal>();
            if (baseCmd is not null) list.AddRange(baseCmd.Decals);
            if (mouldCmd is not null) list.AddRange(mouldCmd.Decals);
            if (list.Count > 0) return Maybe<IReadOnlyList<TextDecal>>.Some(list);
        }

        return metadata.GetProperty(TextEmbossKeys.TextDecals);
    }

    public static MeshMetadata WithTextDecals(this MeshMetadata metadata, IReadOnlyList<TextDecal> decals) =>
        metadata.WithProperty(TextEmbossKeys.TextDecals, decals);
}
