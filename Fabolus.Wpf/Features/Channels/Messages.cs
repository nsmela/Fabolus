using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Features.Channels;

public sealed record AddAirChannelMessage(AirChannel Channel);
public sealed record ClearAirChannelsMessage();
public sealed record RemoveAirChannelMessage(AirChannel Channel);
public sealed record AirChannelsUpdatedMessage(AirChannel[] Channels);
public sealed record ChannelSettingsUpdatedMessage(AirChannel Settings);
public sealed record SetChannelSettingsMessage(AirChannel Settings);
public sealed record SetChannelTypeMessage(ChannelTypes Type);

//selected channel
public sealed record SetSelectedChannelMessage(AirChannel? Channel);
public sealed record SelectedChannelUpdatedMessage(AirChannel settings);

//requests
public class AirChannelsRequestMessage() : RequestMessage<AirChannel[]> { }
public class ChannelsSettingsRequestMessage() : RequestMessage<AirChannel> { }