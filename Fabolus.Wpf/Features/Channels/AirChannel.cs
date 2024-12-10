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

public record AirChannel : Channel {
    public AirChannel() { }

    public AirChannel(ChannelTypes type, Vector3 origin, double height, double diameter, double depth) {
        Type = type;
        Anchor = origin;
        Height = height;
        Diameter = diameter;
        Depth = depth;

        X = origin.X;
        Y = origin.Y;
        Z = origin.Z;

        Build();

        Geometry = Mesh.ToGeometry();
        Geometry = StraightChannelGenerator.Build(this).ToGeometry();
    }

    public Guid GUID => Geometry.GUID;

    public ChannelTypes Type { get; set; }
    public Vector3 Anchor { get; set; }
    public double Height { get; set; }
    public double Diameter { get; set; }
    public double Depth { get; set; }
    public MeshGeometry3D Geometry { get; protected set; }
}
