using Fabolus.Core.AirChannel.Builders;
using Fabolus.Core.AirChannel;
using Fabolus.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;

namespace Fabolus.Wpf.Features.Channels;
public record AngledAirChannel : AirChannel {
    public AngledAirChannel() { }

    public Vector3 Normal { get; set; } = Vector3.UnitX;
    public double TipLength { get; set; } = 10.0;

    public AngledAirChannel(Vector3 origin, Vector3 normal, double height, double diameter, double depth) {
        Anchor = origin;
        Height = height;
        Diameter = diameter;
        Depth = depth;
        Normal = normal;

        X = origin.X;
        Y = origin.Y;
        Z = origin.Z;

        Build();
    }

    public override void Build() {
        Geometry = ChannelGenerator
            .AngledChannel()
            .SetDiameters(1.0, Diameter)
            .SetDirection(Normal.X, Normal.Y, Normal.Z)
            .SetOrigin(X, Y, Z)
            .SetLength(Height)
            .SetTipLength(TipLength)
            .Build()
            .ToGeometry();
    }


}
