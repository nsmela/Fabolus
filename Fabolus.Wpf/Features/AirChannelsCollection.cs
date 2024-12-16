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

        ActiveChannel = this.FirstOrDefault(x => x.Value.Geometry.GUID == id).Key;
    }

    public bool IsActiveChannel(AirChannel channel) => ActiveChannel is not null 
        ? this[ActiveChannel.Value] == channel
        : false;

    public AirChannel? PreviewChannel { get; set; }

    public AirChannelsCollection Add(AirChannel channel) {
        var id = Guid.NewGuid();
        this.Add(id, channel);
        return this;
    }

    public new AirChannelsCollection Clear() {
        this.Clear();
        ActiveChannel = null;
        return this;
    }
}
