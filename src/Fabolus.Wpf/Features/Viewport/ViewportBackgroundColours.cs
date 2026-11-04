using Fabolus.Wpf.Features.AppPreferences;
using System.Windows;
using System.Windows.Media;

namespace Fabolus.Wpf.Features.Viewport;

/// <summary>
/// The colours behind the viewport. The gradient itself is a WPF brush behind a transparent
/// Viewport3DX, so anything inside the scene that has to match it needs the same two colours.
/// </summary>
public static class ViewportBackgroundColours {
    public static (Color Top, Color Bottom) GradientOf(ViewportBackground background) => background switch {
        ViewportBackground.LightSteel => (Color.FromRgb(0xD8, 0xDE, 0xE4), Color.FromRgb(0xB0, 0xB9, 0xC3)),
        _ => (Color.FromRgb(0x1A, 0x1A, 0x1D), Color.FromRgb(0x2D, 0x2D, 0x35)),
    };

    public static LinearGradientBrush CreateBrush(ViewportBackground background) {
        var (top, bottom) = GradientOf(background);
        var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }
}
