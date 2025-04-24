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

    public PathAirChannel? PathChannel() =>
        this.FirstOrDefault(x => x.Value.ChannelType == Core.AirChannel.ChannelTypes.Path).Value as PathAirChannel;

    public void Remove(IAirChannel channel) {
        if (this.ContainsKey(channel.GUID)) {
            this.Remove(channel.GUID);
            return;
        }

        var path = channel as PathAirChannel;
        if (path is null) {
            return;
        }

        this.RemovePaths();
    }

    public AirChannelsCollection RemovePaths() {
        var id = this.FirstOrDefault(x => x.Value.ChannelType == Core.AirChannel.ChannelTypes.Path).Key;
        this.Remove(id);
        return this;

    }

    private void ProcessPathChannel(PathAirChannel channel) {
        if (this.Any(x => x.Value.ChannelType == Core.AirChannel.ChannelTypes.Path)) {
            var id = this.FirstOrDefault(x => x.Value.ChannelType == Core.AirChannel.ChannelTypes.Path).Key;
            this.Remove(id);
            this.Add(channel.GUID, channel); 
        } else { this.Add(channel.GUID, channel); }
    }
}
