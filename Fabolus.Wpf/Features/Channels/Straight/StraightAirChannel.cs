using Fabolus.Core.AirChannel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels.Straight;
public sealed record StraightAirChannel : AirChannel {
    public override ChannelTypes ChannelType => ChannelTypes.Straight;

    public void Build() {
        //call to channel generator
    }
}
