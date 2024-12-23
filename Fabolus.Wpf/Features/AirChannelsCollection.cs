using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Channels.Path;

namespace Fabolus.Wpf.Features;

public class AirChannelsCollection : Dictionary<Guid, IAirChannel> {
    private Guid? ActiveChannel { get; set; } = null;
    public IAirChannel? GetActiveChannel => HasActiveChannel
        ? this[ActiveChannel.Value]
        : null;

    public bool HasActiveChannel => ActiveChannel is not null;

    public void SetActiveChannel(Guid? id) {
        if (id is null) { 
            ActiveChannel = null;
            return;
        }

        if (!this.ContainsKey(id.Value)) { throw new KeyNotFoundException($"Invalid key {id} was used to set active channel"); }

        ActiveChannel = id;
    }

    public bool IsActiveChannel(IAirChannel channel) => HasActiveChannel 
        ? this[ActiveChannel.Value] == channel
        : false;


    public void RemoveActiveChannel() {
        if (ActiveChannel is null) { return; }

        this.Remove(ActiveChannel.Value);
        ActiveChannel = null;
    }

    public IAirChannel? PreviewChannel { get; set; }

    public AirChannelsCollection Add(IAirChannel channel) {
        if (channel.ChannelType == Core.AirChannel.ChannelTypes.Path) { ProcessPathChannel(channel as PathAirChannel); } 
        else { this.Add(channel.GUID, channel); }

        ActiveChannel = channel.GUID;
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
