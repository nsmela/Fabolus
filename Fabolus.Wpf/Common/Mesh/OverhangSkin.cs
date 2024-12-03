using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Common.Mesh;
public static class OverhangSkin {
    public static DiffuseMaterial GetOverHangTexture(float warningAngle, float faultAngle, Color baseColor, Color warningColor, Color faultColor) {
        float lowerOffset = warningAngle / 90.0f;
        float upperOffset = faultAngle / 90.0f;
        float startingOffset = MathF.Max(lowerOffset - 0.1f, 0.0f);

        //gradient color used for overhang display texture
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new System.Windows.Point(0, 0);
        gradientBrush.EndPoint = new System.Windows.Point(0, 1);

        gradientBrush.GradientStops.Add(new GradientStop {
            Color = baseColor,
            Offset = startingOffset
        });
        gradientBrush.GradientStops.Add(new GradientStop {
            Color = warningColor,
            Offset = lowerOffset
        }); ;
        gradientBrush.GradientStops.Add(new GradientStop {
            Color = faultColor,
            Offset = upperOffset
        });
        return new DiffuseMaterial(gradientBrush);

    }


}
