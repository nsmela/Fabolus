using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Channels.Path;

namespace Fabolus.Wpf.Features;

public class AirChannelsCollection : Dictionary<Guid, IAirChannel> {

    public IAirChannel? PreviewChannel { get; set; }

    public AirChannelsCollection Add(IAirChannel channel) {
        if (channel.ChannelType == Core.AirChannel.ChannelTypes.Path) { ProcessPathChannel(channel as PathAirChannel); } 
        else { this.Add(channel.GUID, channel); }

        return this;
    }

    public new AirChannelsCollection Clear() {
        return new AirChannelsCollection();
    }

    private void ProcessPathChannel(PathAirChannel channel) {
        if (this.Any(x => x.Value.ChannelType == Core.AirChannel.ChannelTypes.Path)) {
            var id = this.FirstOrDefault(x => x.Value.ChannelType == Core.AirChannel.ChannelTypes.Path).Key;
            this.Remove(id);
            this.Add(channel.GUID, channel); 
        } else { this.Add(channel.GUID, channel); }
    }
}
