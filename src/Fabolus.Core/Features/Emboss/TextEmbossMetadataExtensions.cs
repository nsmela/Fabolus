using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public static class TextEmbossKeys
{
    public static readonly MetadataKey<TextDecal> TextDecal = new("TextDecal");
}

public static class TextEmbossMetadataExtensions
{
    public static Maybe<TextDecal> TextDecal(this MeshMetadata metadata)
    {
        var command = metadata.Commands.OfType<TextEmbossCommand>().FirstOrDefault();
        if (command is not null)
        {
            return Maybe<TextDecal>.Some(command.Decal);
        }
        return metadata.GetProperty(TextEmbossKeys.TextDecal);
    }

    public static MeshMetadata WithTextDecal(this MeshMetadata metadata, TextDecal decal) =>
        metadata.WithProperty(TextEmbossKeys.TextDecal, decal);
}
