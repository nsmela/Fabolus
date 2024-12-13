using Fabolus.Core;
using Fabolus.Core.AirChannel;
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
    public abstract ChannelTypes ChannelType { get; } 
    public Guid GUID => Geometry.GUID;
    public Vector3 Anchor { get; set; }
    public float Height { get; set; }
    public float Diameter { get; set; }
    public float Depth { get; set; }
    public MeshGeometry3D Geometry { get; protected set; }

}
