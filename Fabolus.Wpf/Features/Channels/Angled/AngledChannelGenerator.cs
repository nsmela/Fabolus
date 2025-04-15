using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common.Extensions;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Features.Channels.Angled;

/// <summary>
/// Create the mesh for the angled channel
/// Tried in fablous.Core with the g3 library, but struggled
/// </summary>
public sealed record AngledChannelGenerator : ChannelGenerator {

    private double BottomDiameter { get; set; } = 1.0;
    private double BottomRadius => BottomDiameter / 2.0;
    private double TipLength { get; set; } = 10.0;

    private AngledChannelGenerator() { }

    //references
    //MeshBuilder: https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.Shared/Geometry/MeshBuilder.cs#L167
    //Vector3 Extensions: https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.SharpDX.Shared/Extensions/Vector3DExtensions.cs#L46

    public static AngledChannelGenerator New() => new();

    public AngledChannelGenerator WithDepth(float depth) => this with { Depth = depth };
    public AngledChannelGenerator WithDiameters(double start, double top) =>
        this with { BottomDiameter = (float)start, Diameter = (float)top };
    public AngledChannelGenerator WithDirection(Vector3 normal) => this with { Normal = normal };
    public AngledChannelGenerator WithHeight(float zHeight) => this with { MaxHeight = zHeight };
    public AngledChannelGenerator WithOrigin(Vector3 origin) => this with { Origin = origin };
    public AngledChannelGenerator WithTipLength(float length) => this with { TipLength = length };

    public override MeshGeometry3D Build() {
        //generate list of points for the angled channel
        var curve = AngledChannelCurve.Curve(
                Origin.ToVector3d(),
                Normal.ToVector3d(),
                TipLength,
                Radius)
            .ToVector3()
            .ToList();

        var mesh = new MeshBuilder();
        var count = curve.Count();
        var last = curve.Last();
        curve.Add(new Vector3 { X = last.X, Y = last.Y, Z = last.Z + 50 });

        var radii = new List<double> { BottomRadius, BottomRadius };
        for (int i = 2; i < curve.Count(); i++) {
            radii.Add(Radius);
        }

        mesh.AddTube(curve, null, radii.ToArray(), 16, false, true, true);

        return mesh.ToMeshGeometry3D();
    }

}
