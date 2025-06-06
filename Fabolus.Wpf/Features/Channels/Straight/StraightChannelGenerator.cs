using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels.Straight;
public sealed record StraightChannelGenerator : ChannelGenerator {
    private float BottomDiameter { get; set; } = 1.0f;
    private float TipLength { get; set; } = 10.0f;

    public static StraightChannelGenerator New() => new();
    public StraightChannelGenerator WithDepth(float depth) => this with { Depth = depth };
    public StraightChannelGenerator WithDiameters(float start, float top) =>
        this with { BottomDiameter = start, Diameter = top };
    public StraightChannelGenerator WithHeight(float zHeight) => this with { MaxHeight = zHeight };
    public StraightChannelGenerator WithOrigin(Vector3 origin) => this with { Origin = origin };
    public StraightChannelGenerator WithTipLength(float length) => this with { TipLength = length };

    public override MeshGeometry3D Build() {
        var points = new List<Vector3> {
            Origin - Vector3.UnitZ * Depth,
            Origin,
            Origin + Vector3.UnitZ * TipLength,
            new Vector3(Origin.X, Origin.Y, MaxHeight)
        };

        var diameters = new double[] {
            BottomDiameter,
            BottomDiameter,
            Diameter,
            Diameter
        };

        var mesh = new MeshBuilder();
        mesh.AddTube(points, null, diameters, 16, false, true, true);
        return mesh.ToMeshGeometry3D();
    }
}
