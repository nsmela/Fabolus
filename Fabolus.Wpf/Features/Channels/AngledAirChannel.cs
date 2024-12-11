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
        Geometry = AngledChannelGenerator.New()
            .WithDepth(1.0)
            .WithDiameters(1.0, Diameter)
            .WithDirection(Normal)
            .WithOrigin(Anchor)
            .WithHeight(Height)
            .WithTipLength(TipLength)
            .Build();

        //Geometry = Fabolus.Core.AirChannel.Builders.ChannelGenerator
         //   .AngledChannel()
          //  .SetDirection(Normal.X, Normal.Y, Normal.Z)
          //  .Build()
           // .ToGeometry();
    }


}
