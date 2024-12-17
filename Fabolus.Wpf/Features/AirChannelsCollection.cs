using Fabolus.Wpf.Features.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features;
public class AirChannelsCollection : Dictionary<Guid, AirChannel> {
    private Guid? ActiveChannel { get; set; } = null;
    public AirChannel? GetActiveChannel => ActiveChannel is not null
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

    public bool IsActiveChannel(AirChannel channel) => ActiveChannel is not null 
        ? this[ActiveChannel.Value] == channel
        : false;


    public void RemoveActiveChannel() {
        if (ActiveChannel is null) { return; }

        this.Remove(ActiveChannel.Value);
        ActiveChannel = null;
    }

    public AirChannel? PreviewChannel { get; set; }

    public AirChannelsCollection Add(AirChannel channel) {
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
