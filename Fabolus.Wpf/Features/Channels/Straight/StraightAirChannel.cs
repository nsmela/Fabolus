using Fabolus.Core.AirChannel;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels.Straight;
public sealed record StraightAirChannel : IAirChannel {
    public StraightAirChannel() : base() { }

    public ChannelTypes ChannelType => ChannelTypes.Straight;
    public float Depth { get; set; } = 1.5f;
    public MeshGeometry3D Geometry { get; set; }
    public Guid GUID { get; init; }
    public float Height { get; set; } = 10.0f;
    public float LowerDiameter { get; set; } = 5.0f;
    public Vector3 Origin { get; set; }
    public float TipLength { get; set; } = 3.0f;
    public float UpperDiameter { get; set; } = 5.0f;

    public IAirChannel ApplySettings(AirChannelSettings settings) {
        var setting = settings[this.ChannelType] as StraightAirChannel;
        var channel = this with {
            Depth = setting.Depth,
            LowerDiameter = setting.LowerDiameter,
            UpperDiameter = setting.UpperDiameter,
            TipLength = setting.TipLength,
        };

        channel.Build();
        return channel;
    }

    public void Build() {
        //call to channel generator
        Geometry = StraightChannelGenerator
            .New()
            .WithDepth(Depth)
            .WithDiameters(LowerDiameter, UpperDiameter)
            .WithHeight(Height)
            .WithOrigin(Origin)
            .WithTipLength(TipLength)
            .Build();
    }

    public IAirChannel New() => this with { GUID = Guid.NewGuid() };

    public IAirChannel WithHit(HitTestResult hit, bool isPreview = false) {
        var result = this with { Origin = hit.PointHit };
        result.Build();
        return result;
    }

}
