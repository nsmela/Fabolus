using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Pages.Smooth;

public static class SkinHelper {

    private static Color4 LowerColor => new Color4(1, 0, 0, 1);
    private static Color4 MidColor => new Color4(0, 0, 0, 1);
    private static Color4 UpperColor => new Color4(0, 0, 1, 1);

    public static Material SurfaceDifferenceSkin(float distance) {
        var lower = (int)distance * 10;
        var upper = (int)distance * 10;
        var offset = 5;

        var lowerSteps = lower - offset;
        var midSteps = offset;

        var endSteps = upper - offset;

        var colors = GetGradients(LowerColor, LowerColor, lowerSteps) //bottom end, lower angle setting
            .Concat(GetGradients(LowerColor, MidColor, offset)) //warning color transition
            .Concat(GetGradients(MidColor, MidColor, midSteps)) //warning color section
            .Concat(GetGradients(MidColor, UpperColor, offset)) //fault color transition, upper angle setting
            .Concat(GetGradients(UpperColor, UpperColor, endSteps)) //fault color section, ends at 90 degrees
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
