using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.AirChannels;

public static class AirChannelKeys {
    public static readonly MetadataKey<IReadOnlyList<IAirChannel>> AirChannels = new("Air Channels");
}

public static class AirChannelMetadataExtensions
{
    public static Maybe<IReadOnlyList<IAirChannel>> AirChannels(this MeshMetadata metadata) =>
        metadata.GetProperty(AirChannelKeys.AirChannels);

    public static MeshMetadata WithAirChannels(this MeshMetadata metadata, IEnumerable<IAirChannel> channels) =>
        metadata.WithProperty(AirChannelKeys.AirChannels, channels.ToList());
}
