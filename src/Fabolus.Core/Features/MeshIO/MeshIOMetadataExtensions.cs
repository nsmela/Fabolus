using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.MeshIO;

public static class MeshIOKeys {
    public static readonly MetadataKey<TopologyValidation> Topology = new("Topology Validation");
    public static readonly MetadataKey<MeshStatistics> Stats = new("Mesh Statistics");
}

public static class MeshIOMetadataExtensions {
    public static Maybe<MeshStatistics> MeshStats(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) =>
        metadataBase.AsFabolus().GetProperty(MeshIOKeys.Stats);

    public static Maybe<TopologyValidation> Topology(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) =>
        metadataBase.AsFabolus().GetProperty(MeshIOKeys.Topology);

    public static MeshMetadata WithMeshStats(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, MeshStatistics stats) =>
        metadataBase.AsFabolus().WithProperty(MeshIOKeys.Stats, stats);

    public static MeshMetadata WithTopology(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, TopologyValidation topology) =>
        metadataBase.AsFabolus().WithProperty(MeshIOKeys.Topology, topology);


}
