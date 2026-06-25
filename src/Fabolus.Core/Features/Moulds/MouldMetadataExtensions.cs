using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public static class MouldKeys {
    public static readonly MetadataKey<MouldDefinition> Mould = new("Mould");
}

public static class MouldMetadataExtensions
{
    public static Maybe<MouldDefinition> MouldDefinition(this MeshMetadata metadata) =>
        metadata.GetProperty(MouldKeys.Mould);

    public static MeshMetadata WithMouldDefinition(this MeshMetadata metadata, MouldDefinition definition) =>
        metadata.WithProperty(MouldKeys.Mould, definition);
}
