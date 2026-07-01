using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public static class MouldKeys {
    // Set only on a mesh that IS a generated mould result (by GenerateMould). Its
    // presence is the signal that this mesh is a mould, not a mesh being edited.
    public static readonly MetadataKey<MouldDefinition> Mould = new("Mould");

    // Set on a mesh that is still being edited in the Mould tab, to preserve
    // in-progress settings/channels across tab switches before Generate is clicked.
    public static readonly MetadataKey<MouldDefinition> PendingMould = new("Pending Mould");
}

public static class MouldMetadataExtensions
{
    public static Maybe<MouldDefinition> MouldDefinition(this MeshMetadata metadata) =>
        metadata.GetProperty(MouldKeys.Mould);

    public static MeshMetadata WithMouldDefinition(this MeshMetadata metadata, MouldDefinition definition) =>
        metadata.WithProperty(MouldKeys.Mould, definition);

    public static Maybe<MouldDefinition> PendingMouldDefinition(this MeshMetadata metadata) =>
        metadata.GetProperty(MouldKeys.PendingMould);

    public static MeshMetadata WithPendingMouldDefinition(this MeshMetadata metadata, MouldDefinition definition) =>
        metadata.WithProperty(MouldKeys.PendingMould, definition);
}
