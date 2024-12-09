using Fabolus.Core.AirChannel.Builders;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel;

public sealed record Channel { 
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Height { get; set; }
    public double Diameter { get; set; }
    public double Depth { get; set; }
    public ChannelTypes ChannelType { get; set; } = ChannelTypes.Straight;

    public Vector3d Origin => new Vector3d(X, Y, Z);
    public DMesh3 Mesh { get; private set; }

    public void Build() {
        Mesh = StraightChannelGenerator.Build(this);
    }
}
