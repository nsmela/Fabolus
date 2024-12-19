using Fabolus.Wpf.Features.Channels;

namespace Fabolus.Wpf.Features;

public class AirChannelsCollection : Dictionary<Guid, IAirChannel> {
    private Guid? ActiveChannel { get; set; } = null;
    public IAirChannel? GetActiveChannel => ActiveChannel is not null
        ? this[ActiveChannel.Value]
        : null;

    public void SetActiveChannel(Guid? id) {
        if (id is null) { 
            ActiveChannel = null;
            return;
        }

        if (!this.ContainsKey(id.Value)) { throw new KeyNotFoundException($"Invalid key {id} was used to set active channel"); }

        ActiveChannel = id;
    }

    public bool IsActiveChannel(IAirChannel channel) => ActiveChannel is not null 
        ? this[ActiveChannel.Value] == channel
        : false;


    public void RemoveActiveChannel() {
        if (ActiveChannel is null) { return; }

        this.Remove(ActiveChannel.Value);
        ActiveChannel = null;
    }

    public IAirChannel? PreviewChannel { get; set; }

    public AirChannelsCollection Add(IAirChannel channel) {
        //var id = Guid.NewGuid();
        //channel = channel with { GUID = id };
        this.Add(channel.GUID, channel);
        ActiveChannel = channel.GUID;
        return this;
    }

    public new AirChannelsCollection Clear() {
        return new AirChannelsCollection();
    }
}
