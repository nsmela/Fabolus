using Fabolus.Core.AirChannel;
using SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Fabolus.Wpf.Features.Channels.Straight;

namespace Fabolus.Wpf.Features.Channels.Angled;

public sealed record AngledAirChannel : IAirChannel {
    public Vector3 Anchor { get; set; }
    public float BottomDiameter { get; set; } = 4.0f;
    public ChannelTypes ChannelType => ChannelTypes.AngledHead;
    public float Diameter { get; set; } = 6.0f;
    public float Depth { get; set; } = 0.5f;
    public MeshGeometry3D Geometry { get; set; }
    public Guid GUID { get; init; }
    public float Height { get; set; } = 5.0f;
    public Vector3 Normal { get; set; } = Vector3.UnitZ;
    public float TipLength { get; set; } = 6.0f;

    public AngledAirChannel() { }

    public IAirChannel ApplySettings(AirChannelSettings settings) {
        var setting = settings[this.ChannelType] as AngledAirChannel;
        var channel = this with {
            Depth = setting.Depth,
            BottomDiameter = setting.BottomDiameter,
            Diameter = setting.Diameter,
            TipLength = setting.TipLength,
        };

        channel.Build();
        return channel;
    }

    public void Build() {
        Geometry = AngledChannelGenerator.New()
            .WithDepth(Depth)
            .WithDiameters(BottomDiameter, Diameter)
            .WithDirection(Normal)
            .WithOrigin(Anchor)
            .WithHeight(Height)
            .WithTipLength(TipLength)
            .Build();

    }

    public IAirChannel New() => this with { GUID = Guid.NewGuid() };

    public IAirChannel WithHit(HitTestResult hit, bool isPreview = false) {
        var result = this with { Anchor = hit.PointHit, Normal = hit.NormalAtHit };
        result.Build();
        return result;
    }

}
