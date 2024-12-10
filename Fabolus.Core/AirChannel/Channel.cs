using Fabolus.Core.AirChannel.Builders;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel;

public record Channel {
    private const double DEFAULT_HEIGHT = 100;
    private const double DEFAULT_DIAMETER = 5.0;
    private const double DEFAULT_DEPTH = 5.0;

    public double X { get; set; } = 0.0;
    public double Y { get; set; } = 0.0;
    public double Z { get; set; } = 0.0;
    public double Height { get; set; } = DEFAULT_HEIGHT;
    public double Diameter { get; set; } = DEFAULT_DIAMETER;
    public double Depth { get; set; } = DEFAULT_DEPTH;
    public ChannelTypes ChannelType { get; set; } = ChannelTypes.Straight;

    public Vector3d Origin => new Vector3d(X, Y, Z);
    public DMesh3? Mesh { get; protected set; }

    public Channel() {
        Build();
    }

    public virtual void Build() {
        Mesh = StraightChannelGenerator.Build(this);
    }
}

public record AngledChannel : Channel {
    public double BottomDiameter { get; set; } = 1.0;
    public Vector3d Normal { get; set; } = Vector3d.Zero;

    public AngledChannel() {
        Build();
    }

    public override void Build() {
        var mesh = ChannelGenerator
            .AngledChannel()
            .SetDiameters(BottomDiameter, Diameter)
            .SetDirection(Normal)
            .Build();
            
    }
}