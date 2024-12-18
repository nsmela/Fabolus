using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Features.Channels.Angled;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels.Path;
public record PathAirChannel : AirChannel {
    public override ChannelTypes ChannelType => ChannelTypes.Path;
    public List<Vector3> PathPoints { get; set; } = [];
    public float UpperDiameter { get; set; } = 8.0f;
    public float UpperHeight { get; set; } = 5.0f;
    public float TopHeight { get; set; } = 20.0f;

    //get data from the Hit result and build the mesh
    public override AirChannel WithHit(HitTestResult hit) {
        PathPoints = new() { hit.PointHit };
        var result = this with { PathPoints = this.PathPoints };
        result.Build();
        return result;
    }

    public void Build() {
        Geometry = PathChannelGenerator.New()
            .WithDepth(Depth)
            .WithHeight(Height, UpperHeight, TopHeight)
            .WithPath(PathPoints.ToArray())
            .WithRadius(Diameter / 2.0f, UpperDiameter / 2.0f)
            .Build();

    }
}
