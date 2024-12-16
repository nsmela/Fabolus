using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Features.Channels;

public sealed record AirChannelsUpdatedMessage(AirChannelsCollection Channels);
public sealed record ChannelSettingsUpdatedMessage(AirChannelSettings Settings);


//requests
public class AirChannelsRequestMessage() : RequestMessage<AirChannelsCollection> { }
public class ChannelsSettingsRequestMessage() : RequestMessage<AirChannelSettings> { }