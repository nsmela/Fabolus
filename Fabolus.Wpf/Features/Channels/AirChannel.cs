using Fabolus.Core;
using Fabolus.Core.AirChannel;
using Fabolus.Core.AirChannel.Builders;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Features.Channels;

public class AirChannel {

    public AirChannel(ChannelTypes type, Vector3 origin, double height, double diameter, double depth) {
        Type = type;
        Origin = origin;
        Height = height;
        Diameter = diameter;
        Depth = depth;

        Geometry = StraightChannelGenerator.Build(new Channel {
            X = this.Origin.X,
            Y = this.Origin.Y,
            Z = this.Origin.Z,
            ChannelType = this.Type,
            Depth = this.Depth,
            Diameter = this.Diameter,
            Height = this.Height,
        }).ToGeometry();
    }

    public Guid GUID => Geometry.GUID;

    public ChannelTypes Type { get; set; }
    public Vector3 Origin { get; set; }
    public double Height { get; set; }
    public double Diameter { get; set; }
    public double Depth { get; set; }
    public MeshGeometry3D Geometry { get; private set; }
}
