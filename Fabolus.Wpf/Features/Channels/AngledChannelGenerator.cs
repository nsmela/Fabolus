using Fabolus.Core.AirChannel;
using Fabolus.Core.AirChannel.Builders;
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
    private double RefAngle => 2.5 * Math.PI - Angle; //2D angle 
    private double BottomDiameter { get; set; } = 1.0;
    private double BottomRadius => BottomDiameter / 2;
    private double TipLength { get; set; } = 10.0;

    private AngledChannelGenerator() { }

    //references
    //MeshBuilder: https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.Shared/Geometry/MeshBuilder.cs#L167
    //Vector3 Extensions: https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.SharpDX.Shared/Extensions/Vector3DExtensions.cs#L46

    public static AngledChannelGenerator New() => new();
    private Vector2 ConvertDirection(Vector3 direction) {
        var angle = Vector3.UnitZ.AngleBetween(direction);
        var refAngle = 2.5 * Math.PI - angle;

        return new Vector2 {
            X = (float)Math.Cos(refAngle),
            Y = (float)Math.Sin(refAngle)
        };
    }

    public AngledChannelGenerator WithDepth(double depth) => this with { Depth = depth };
    public AngledChannelGenerator WithDiameters(double start, double top) =>
        this with { BottomDiameter = start, Diameter = (float)top };
    public AngledChannelGenerator WithDirection(Vector3 normal) => this with { Normal = normal };
    public AngledChannelGenerator WithHeight(double zHeight) => this with { MaxHeight = zHeight };
    public AngledChannelGenerator WithOrigin(Vector3 origin) => this with { Origin = origin };
    public AngledChannelGenerator WithTipLength(double length) => this with { TipLength = length };

    public override MeshGeometry3D Build() {

        var (curve, radii) = GetConeValues();

        var mesh = new MeshBuilder();
        mesh.AddTube(curve, null, radii, SEGMENTS, false, true, true);

        return mesh.ToMeshGeometry3D();

    }

    private (List<Vector3> points, double[] radii) GetBendValues() {

    }

    private (List<Vector3> points, double[] radii) GetConeValues() {
        var direction = ConvertDirection(this.Normal);

        List<Vector2> points = [
            Vector2.Zero - direction * (float)this.Depth,
            Vector2.Zero,
            Vector2.Zero + direction * (float)this.TipLength];

        List<double> radii = [
            this.BottomRadius,
            this.BottomRadius,
            this.Diameter];

        return (points.Select(x => new Vector3(x.X, 0, x.Y)).ToList(), radii.ToArray());
    }

}
