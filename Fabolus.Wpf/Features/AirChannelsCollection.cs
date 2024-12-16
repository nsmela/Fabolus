using Fabolus.Wpf.Features.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features;
public class AirChannelsCollection : Dictionary<Guid, AirChannel> {
    private Guid? ActiveChannel { get; set; } = null;

    public AirChannelsCollection Add(AirChannel channel) {
        var id = Guid.NewGuid();
        this.Add(id, channel);
        return this;
    }

}
