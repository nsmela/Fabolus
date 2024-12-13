using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fabolus.Wpf.Features.Channels;

public sealed record AddAirChannelMessage(AirChannel channel);
public sealed record ClearAirChannelsMessage();
public sealed record RemoveAirChannelMessage(AirChannel channel);
public sealed record AirChannelsUpdatedMessage(AirChannel[] channels);
public sealed record ChannelSettingsUpdatedMessage(AirChannel settings);

public class AirChannelsRequestMessage() : RequestMessage<AirChannel[]> { }
public class ChannelsSettingsRequestMessage() : RequestMessage<AirChannel> { }