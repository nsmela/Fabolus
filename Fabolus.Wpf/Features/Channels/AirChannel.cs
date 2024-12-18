using Fabolus.Core;
using Fabolus.Core.AirChannel;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Features.Channels;

public abstract record AirChannel {
    public AirChannel() { }

    public Guid GUID { get; init; } = Guid.NewGuid();
    public abstract ChannelTypes ChannelType { get; }
    public static abstract AirChannel New();
    public float Height { get; set; }
    public float Diameter { get; set; } = 5.0f;
    public float Depth { get; set; } = 0.5f;
    public abstract AirChannel WithHit(HitTestResult hit, bool isPreview = false);
    public MeshGeometry3D Geometry { get; protected set; }

}
