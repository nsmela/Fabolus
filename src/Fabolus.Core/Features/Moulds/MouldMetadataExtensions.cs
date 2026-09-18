using BasicResults;
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
    public static Maybe<MouldDefinition> MouldDefinition(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) {
        var definition = metadataBase.AsFabolus().Commands.OfType<MouldDefinition>().FirstOrDefault();
        return definition is null ? Maybe<MouldDefinition>.None() : Maybe<MouldDefinition>.Some(definition);
    }

    public static MeshMetadata WithMouldDefinition(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, MouldDefinition definition) =>
        metadataBase.AsFabolus().WithCommand(definition);

    public static Maybe<MouldDefinition> PendingMouldDefinition(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) =>
        metadataBase.AsFabolus().GetProperty(MouldKeys.PendingMould);

    public static MeshMetadata WithPendingMouldDefinition(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, MouldDefinition definition) =>
        metadataBase.AsFabolus().WithProperty(MouldKeys.PendingMould, definition);
}
