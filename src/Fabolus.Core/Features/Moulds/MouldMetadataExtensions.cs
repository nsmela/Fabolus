using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;
using System.Linq;

namespace Fabolus.Core.Features.Moulds;

public static class MouldKeys {
    // Set on a mesh that is still being edited in the Mould tab, to preserve
    // in-progress settings/channels across tab switches before Generate is clicked.
    public static readonly MetadataKey<MouldDefinition> PendingMould = new("Pending Mould");
}

public static class MouldMetadataExtensions
{
    // Presence of a MouldDefinition in Commands is the signal that this mesh is a mould,
    // not a mesh being edited - matches today's "Mould = generated result" semantics.
    public static Maybe<MouldDefinition> MouldDefinition(this MeshMetadata metadata) {
        var definition = metadata.Commands.OfType<MouldDefinition>().FirstOrDefault();
        return definition is null ? Maybe<MouldDefinition>.None() : Maybe<MouldDefinition>.Some(definition);
    }

    public static MeshMetadata WithMouldDefinition(this MeshMetadata metadata, MouldDefinition definition) =>
        metadata.WithCommand(definition);

    public static Maybe<MouldDefinition> PendingMouldDefinition(this MeshMetadata metadata) =>
        metadata.GetProperty(MouldKeys.PendingMould);

    public static MeshMetadata WithPendingMouldDefinition(this MeshMetadata metadata, MouldDefinition definition) =>
        metadata.WithProperty(MouldKeys.PendingMould, definition);
}
