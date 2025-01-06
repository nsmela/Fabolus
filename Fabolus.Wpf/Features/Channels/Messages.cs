using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fabolus.Wpf.Features.Channels;

//updates
public sealed record AirChannelsUpdatedMessage(AirChannelsCollection Channels);
public sealed record ChannelSettingsUpdatedMessage(AirChannelSettings Settings);
public sealed record ActiveChannelUpdatedMessage(IAirChannel Channel);


//requests
public class ActiveChannelRequestMessage() : RequestMessage<IAirChannel> { }
public class AirChannelsRequestMessage() : RequestMessage<AirChannelsCollection> { }
public class ChannelsSettingsRequestMessage() : RequestMessage<AirChannelSettings> { }