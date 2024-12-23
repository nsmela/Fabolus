using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Features.Channels;

//updates
public sealed record AirChannelsUpdatedMessage(AirChannelsCollection Channels);
public sealed record ChannelSettingsUpdatedMessage(AirChannelSettings Settings);

//setters
public sealed record ChannelTypeSetMessage(ChannelTypes? Type);
public sealed record ChannelSettingsSetMessage(AirChannelSettings Settings);
public sealed record ActiveChannelSetMessage(Guid? ChannelId);
public sealed record ChannelAddMessage(IAirChannel Channel);
public sealed record ChannelRemoveMessage(Guid? Id);
public sealed record ChannelClearMessage();


//requests
public class AirChannelsRequestMessage() : RequestMessage<AirChannelsCollection> { }
public class ChannelsSettingsRequestMessage() : RequestMessage<AirChannelSettings> { }