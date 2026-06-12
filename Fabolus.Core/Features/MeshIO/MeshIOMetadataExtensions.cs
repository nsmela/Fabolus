using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.MeshIO;

public static class MeshIOKeys {
    public static readonly MetadataKey<TopologyValidation> Topology = new("Topology Validation");
}

public static class MeshIOMetadataExtensions {
    public static Result<TopologyValidation> Topology(this MeshMetadata metadata) =>
    metadata.GetProperty(MeshIOKeys.Topology);

    public static MeshMetadata WithTopology(this MeshMetadata metadata, TopologyValidation topology) =>
        metadata.WithProperty(MeshIOKeys.Topology, topology);
}
