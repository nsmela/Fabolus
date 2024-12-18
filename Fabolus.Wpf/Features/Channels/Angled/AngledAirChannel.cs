using Fabolus.Core.AirChannel;
using Fabolus.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Channels.Angled;
public record AngledAirChannel : IAirChannel {
    public ChannelTypes ChannelType => ChannelTypes.AngledHead;
    public Vector3 Anchor { get; set; }
    public float Depth { get; set; } = 0.5f;
    public float Diameter { get; set; } = 4.0f;
    public float Height { get; set; } = 5.0f;
    public Vector3 Normal { get; set; } = Vector3.UnitZ;
    public float TipLength { get; set; } = 6.0f;
    public float BottomDiameter { get; set; } = 5.0f;
    public Guid GUID { get; init; }
    public MeshGeometry3D Geometry { get; set; }

    public AngledAirChannel() { }
    public AngledAirChannel(Vector3 origin, Vector3 normal, float height, float diameter, float depth) {
        Anchor = origin;
        Height = height;
        Diameter = diameter;
        Depth = depth;
        Normal = normal;

        Build();
    }

    public IAirChannel WithHit(HitTestResult hit, bool isPreview = false) {
        var result = this with { Anchor = hit.PointHit, Normal = hit.NormalAtHit };
        result.Build();
        return result;
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

    public static AirChannel New() {
        throw new NotImplementedException();
    }

    AirChannel IAirChannel.WithHit(HitTestResult hit, bool isPreview) {
        throw new NotImplementedException();
    }
}
