using Fabolus.Core;
using Fabolus.Core.AirChannel;
using Fabolus.Core.AirChannel.Builders;
using g3;
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
        var (curve, radii) = GetConeValues();
        var mesh = new MeshBuilder();
        mesh.AddTube(curve, null, radii.ToArray(), SEGMENTS, false, true, true);

        mesh.AddArrow(Origin, Origin + Normal * (float)TipLength, 0.5);
        var (bend, radiii) = BendValues();
        if (bend.Count > 0) { mesh.AddBox(bend[0], 1.0, 1.0, 1.0); }

        foreach(var point in bend) {
            mesh.AddSphere(point, 0.5);
        }

        return mesh.ToMeshGeometry3D();
    }

    private (List<Vector3> points, List<double> radii) GetBendValues() {
        var direction = Normal;
        var radius = (float)(Radius + 1.0f); //offset the radius
        var origin = Origin + Normal * (float)TipLength;
        var circleCentre = new Vector3(origin.X, origin.Y, origin.Z + radius);
        var span = Math.PI / (double)SEGMENTS;

        var points = new List<Vector3>();
        var point = new Vector3(origin.X, origin.Y, origin.Z) + direction;
        var limit = circleCentre.Z;
        var offsetVector = new Vector3 {
            X = (float)Math.Cos(span) * radius * direction.X,
            Y = (float)Math.Cos(span) * radius * direction.Y,
            Z = (float)Math.Sin(span) * radius,
        };

        for (int i = 1; point.Z < circleCentre.Z; i++) {
            points.Add(point);
            var offset = new Vector3 {
                X = (float)Math.Cos(span * i) * radius * direction.X,
                Y = (float)Math.Cos(span * i) * radius * direction.Y,
                Z = (float)Math.Sin(span * i) * radius,
            };
            point += offset;
        }


        var radii = points.Select(x => Radius).ToList(); //creates an array of doubles the length of points

        return (points, radii);
    }

    private (List<Vector3> points, List<double> radii) BendValues() {
        var origin = Origin + Normal * (float)TipLength;
        var bendOrigin = new Vector3d {
            x = origin.X,
            y = origin.Y,
            z = origin.Z
        };
        var vectors = Fabolus.Core.AirChannel.AngledChannelCurve.Curve2(bendOrigin, new g3.Vector3d(Normal.X, Normal.Y, Normal.Z), Radius, 1.0);
        var points = vectors.Select(x => new Vector3((float)x.x, (float)x.y, (float)x.z)).ToList();
        var radii = points.Select(x => Radius).ToList(); //creates an array of doubles the length of points

        return (points, radii);
    }

    private (List<Vector3> points, List<double> radii) GetConeValues() {
        var direction = ConvertDirection;

        var points = new List<Vector3> {
            Origin - this.Normal * (float)this.Depth,
            Origin,
            Origin + Normal * (float)this.TipLength,
        };

        List<double> radii = [
            this.BottomRadius,
            this.BottomRadius,
            this.Radius];

        return (points, radii.ToList());
    }

}
