using Fabolus.Core.AirChannel;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels.Straight;
public sealed record StraightAirChannel : AirChannel {
    public StraightAirChannel() : base() { }

    public override ChannelTypes ChannelType => ChannelTypes.Straight;
    public Vector3 Anchor { get; set; }
    public float TipLength { get; set; } = 4.0f;
    public float BottomDiameter { get; set; } = 4.0f;
    public float BottomRadius => BottomDiameter / 2;
    public override AirChannel WithHit(HitTestResult hit, bool isPreview = false) {
        var result = this with { Anchor = hit.PointHit };
        result.Build();
        return result;
    }

    public void Build() {
        //call to channel generator
        Geometry = StraightChannelGenerator
            .New()
            .WithDepth(Depth)
            .WithDiameters(BottomDiameter, Diameter)
            .WithHeight(Height)
            .WithOrigin(Anchor)
            .WithTipLength(TipLength)
            .Build();
    }

}
