using BasicResults;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.AirChannels;

public static class AirChannelKeys {
    public static readonly MetadataKey<IReadOnlyList<IAirChannel>> AirChannels = new("Air Channels");
}

public static class AirChannelMetadataExtensions
{
    public static Maybe<IReadOnlyList<IAirChannel>> AirChannels(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) =>
        metadataBase.AsFabolus().GetProperty(AirChannelKeys.AirChannels);

    public static MeshMetadata WithAirChannels(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, IEnumerable<IAirChannel> channels) =>
        metadataBase.AsFabolus().WithProperty(AirChannelKeys.AirChannels, channels.ToList());
}
