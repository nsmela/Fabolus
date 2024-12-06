using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows.Media.Media3D;
using Vector2Collection = HelixToolkit.Wpf.SharpDX.Vector2Collection;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;


namespace Fabolus.Wpf.Common.Mesh;
public static class OverhangsHelper {
    private static Color4 BaseColor => new Color4(0.8f, 0.8f, 0.8f, 1);
    private static Color4 WarningColor => new Color4(1, 1, 0, 1);
    private static Color4 FaultColor => new Color4(1, 0, 0, 1);

    public static HelixToolkit.Wpf.SharpDX.Material CreateOverhangsMaterial(float lowerAngle, float upperAngle) {
        var max = 90;
        var offset = 10;

        var lower = (int)lowerAngle;
        var upper = upperAngle < max ? (int)upperAngle : max - 2;

        var lowerSteps = lower;
        var upperSteps = upper - lowerSteps - offset;
        var endSteps = max - upper;

        var colors = GetGradients(BaseColor, BaseColor, lowerSteps) //bottom end, lower angle setting
            .Concat(GetGradients(BaseColor, WarningColor, offset)) //warning color transition
            .Concat(GetGradients(WarningColor, WarningColor, upperSteps)) //warning color section
            .Concat(GetGradients(WarningColor, FaultColor, offset)) //fault color transition, upper angle setting
            .Concat(GetGradients(FaultColor, FaultColor, endSteps)) //fault color section, ends at 90 degrees
            .ToList();

        return new ColorStripeMaterial {
            ColorStripeX = colors,
            ColorStripeY = colors
        };
    }

    public static Vector2Collection GetTextureCoordinates(MeshGeometry3D mesh, Vector3 refAxis) {
        var axis = new Vector3D(refAxis.X, refAxis.Y, refAxis.Z);
        var normals = new Vector3DCollection();
        mesh.Normals.ForEach(n => normals.Add(new Vector3D(n.X, n.Y, n.Z)));

        var result = MeshSkins.GetTextureCoords(new System.Windows.Media.Media3D.MeshGeometry3D { Normals = normals }, axis);
        var textureCoordinates = new Vector2Collection();
        foreach(var coord in result) {
            textureCoordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
        }
        return textureCoordinates;
    }

    private static IEnumerable<Color4> GetGradients(Color4 start, Color4 end, int steps) {
        float stepA = ((end.Alpha - start.Alpha) / (steps - 1));
        float stepR = ((end.Red - start.Red) / (steps - 1));
        float stepG = ((end.Green - start.Green) / (steps - 1));
        float stepB = ((end.Blue - start.Blue) / (steps - 1));

        for (int i = 0; i < steps; i++) {
            yield return new Color4((start.Red + (stepR * i)),
                                        (start.Green + (stepG * i)),
                                        (start.Blue + (stepB * i)),
                                        (start.Alpha + (stepA * i)));
        }
    }
}

