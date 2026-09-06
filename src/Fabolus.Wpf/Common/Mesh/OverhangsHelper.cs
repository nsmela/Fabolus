using HelixToolkit.Wpf.SharpDX;
using SharpDX;

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

