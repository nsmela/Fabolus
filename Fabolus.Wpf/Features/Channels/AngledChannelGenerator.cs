using Fabolus.Core;
using Fabolus.Core.AirChannel;
using Fabolus.Core.AirChannel.Builders;
using Fabolus.Wpf.Common.Extensions;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels;

/// <summary>
/// Create the mesh for the angled channel
/// Tried in fablous.Core with the g3 library, but struggled
/// </summary>
public sealed record AngledChannelGenerator : ChannelGenerator {

    private double Angle => Vector3.UnitZ.AngleBetween(this.Normal); //radians
    private double RefAngle => (2.5 * Math.PI) - Angle; //2D angle 
    private double BottomDiameter { get; set; } = 1.0;
    private double BottomRadius => BottomDiameter / 2;
    private double TipLength { get; set; } = 10.0;

    private AngledChannelGenerator() { }

    //references
    //MeshBuilder: https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.Shared/Geometry/MeshBuilder.cs#L167
    //Vector3 Extensions: https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.SharpDX.Shared/Extensions/Vector3DExtensions.cs#L46

    public static AngledChannelGenerator New() => new();
    private Vector2 ConvertDirection => 
        new Vector2 {
            X = (float)Math.Cos(this.RefAngle),
            Y = (float)Math.Sin(this.RefAngle)
        };
    
    public AngledChannelGenerator WithDepth(double depth) => this with { Depth = depth };
    public AngledChannelGenerator WithDiameters(double start, double top) =>
        this with { BottomDiameter = (float)start, Diameter = (float)top };
    public AngledChannelGenerator WithDirection(Vector3 normal) => this with { Normal = normal };
    public AngledChannelGenerator WithHeight(double zHeight) => this with { MaxHeight = zHeight };
    public AngledChannelGenerator WithOrigin(Vector3 origin) => this with { Origin = origin };
    public AngledChannelGenerator WithTipLength(double length) => this with { TipLength = length };

    public override MeshGeometry3D Build() {
        var curve = Fabolus.Core.AirChannel.AngledChannelCurve.Curve(
            Origin.ToVector3d(),
            Normal.ToVector3d(),
            TipLength,
            Radius)
            .ToVector3()
            .ToList();

        var radii = new List<double> { BottomRadius, BottomRadius };
        for (int i = 2; i < curve.Count(); i++) {
            radii.Add(Radius);
        }

        var mesh = new MeshBuilder();
        var count = curve.Count();
        var last = curve.Last();
        curve.Add(new Vector3 { X = last.X, Y = last.Y, Z = last.Z + 50 });
        for (int i = 0; i < count; i++) {
            mesh.AddArrow(curve[i], curve[i + 1], 0.5);
        }
        return mesh.ToMeshGeometry3D();
    }

}
