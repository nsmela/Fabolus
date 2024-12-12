using Fabolus.Core.AirChannel.Builders;
using Fabolus.Core.AirChannel;
using Fabolus.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;

namespace Fabolus.Wpf.Features.Channels.Angled;
public record AngledAirChannel : AirChannel {
    public AngledAirChannel() { }
    public override ChannelTypes ChannelType => ChannelTypes.AngledHead;
    public Vector3 Normal { get; set; } = Vector3.UnitZ;
    public float TipLength { get; set; } = 10.0f;

    public AngledAirChannel(Vector3 origin, Vector3 normal, float height, float diameter, float depth) {
        Anchor = origin;
        Height = height;
        Diameter = diameter;
        Depth = depth;
        Normal = normal;

        Build();
    }

    public void Build() {
        Geometry = AngledChannelGenerator.New()
            .WithDepth(1.0f)
            .WithDiameters(1.0, Diameter)
            .WithDirection(Normal)
            .WithOrigin(Anchor)
            .WithHeight(Height)
            .WithTipLength(TipLength)
            .Build();

    }


}
