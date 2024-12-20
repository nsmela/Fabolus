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
public record PathAirChannel : IAirChannel {
    public ChannelTypes ChannelType => ChannelTypes.Path;
    public float Depth { get; set; } = 0.5f;
    public MeshGeometry3D Geometry { get; set; }
    public Guid GUID { get; init; }
    public float Height { get; set; } = 5.0f;
    public float LowerDiameter { get; set; } = 3.0f;
    public float LowerHeight { get; set; } = 20.0f;
    public List<Vector3> PathPoints { get; set; } = [];
    public float UpperDiameter { get; set; } = 8.0f;
    public float UpperHeight { get; set; } = 5.0f;

    //get data from the Hit result and build the mesh
    public IAirChannel WithHit(HitTestResult hit, bool isPreview = false) {
        if (isPreview) { PathPoints = new() { hit.PointHit }; }
        else { PathPoints.Add(hit.PointHit); }

        var result = this with { PathPoints = this.PathPoints };
        result.Build();
        return result;
    }

    public void Build() {
        Geometry = PathChannelGenerator.New()
            .WithDepth(Depth)
            .WithLength(Height, UpperHeight, Height)
            .WithPath(PathPoints.ToArray())
            .WithRadius(LowerDiameter / 2.0f, UpperDiameter / 2.0f)
            .Build();

    }

    public IAirChannel New() => this with { GUID = Guid.NewGuid() };
}
