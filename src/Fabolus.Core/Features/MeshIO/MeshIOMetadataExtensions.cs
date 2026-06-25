using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.MeshIO;

public static class MeshIOKeys {
    public static readonly MetadataKey<TopologyValidation> Topology = new("Topology Validation");
    public static readonly MetadataKey<MeshStatistics> Stats = new("Mesh Statistics");
}

public static class MeshIOMetadataExtensions {
    public static Maybe<MeshStatistics> MeshStats(this MeshMetadata metadata) =>
        metadata.GetProperty(MeshIOKeys.Stats);

    public static Maybe<TopologyValidation> Topology(this MeshMetadata metadata) =>
        metadata.GetProperty(MeshIOKeys.Topology);

    public static MeshMetadata WithMeshStats(this MeshMetadata metadata, MeshStatistics stats) =>
        metadata.WithProperty(MeshIOKeys.Stats, stats);

    public static MeshMetadata WithTopology(this MeshMetadata metadata, TopologyValidation topology) =>
        metadata.WithProperty(MeshIOKeys.Topology, topology);


}
