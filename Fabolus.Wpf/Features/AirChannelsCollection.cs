using Fabolus.Wpf.Features.Channels;

namespace Fabolus.Wpf.Features;

public class AirChannelsCollection : Dictionary<Guid, IAirChannel> {

    public IAirChannel? PreviewChannel { get; set; }

    public AirChannelsCollection Add(IAirChannel channel) {
        this.Add(channel.GUID, channel); 

        return this;
    }

    public new AirChannelsCollection Clear() {
        return new AirChannelsCollection();
    }

    public void Remove(IAirChannel channel) {
        if (this.ContainsKey(channel.GUID)) {
            this.Remove(channel.GUID);
        }
    }

}
