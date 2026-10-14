
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using SharpDX.Direct3D11;
using System.Windows.Media;

namespace Fabolus.Wpf.Features.Viewport;
public static class SceneHelpers {

    public static Element3D GenerateGrid(float width = 250, float depth = 250, float spacing = 10, bool isVisible = true) {
        var grid = new LineBuilder();

        float minX = -width / 2f;
        float maxX = width / 2f;
        float minY = -depth / 2f;
        float maxY = depth / 2f;

        for (int i = 0; i <= width / spacing; i++) {
            grid.AddLine(
                new SharpDX.Vector3(minX + spacing * i, minY, 0),
                new SharpDX.Vector3(minX + spacing * i, maxY, 0));
        }

        for (int i = 0; i <= depth / spacing; i++) {
            grid.AddLine(
                new SharpDX.Vector3(minX, minY + spacing * i, 0),
                new SharpDX.Vector3(maxX, minY + spacing * i, 0));
        }

        return new LineGeometryModel3D {
            Geometry = grid.ToLineGeometry3D(),
            IsHitTestVisible = false,
            Visibility = isVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed
        };
    }

    /// <summary>
    /// The positive halves of the X and Y axes, drawn on the bed from the origin out to its near
    /// edges. Red for +X and green for +Y, matching the orientation widget in the corner of the
    /// viewport and the rotation gizmo rings, so the same colour means the same axis everywhere.
    /// </summary>
    /// <remarks>
    /// Returned as separate visuals because a LineGeometryModel3D carries one Color for the whole
    /// geometry - two colours means two models.
    /// </remarks>
    public static IReadOnlyList<Element3D> GenerateBedAxes(float width = 250, float depth = 250, bool isVisible = true) => [
        AxisLine(new SharpDX.Vector3(width / 2f, 0f, AxisLift), Colors.Red, isVisible),
        AxisLine(new SharpDX.Vector3(0f, depth / 2f, AxisLift), Colors.Green, isVisible),
    ];

    // The grid lands a line exactly on an axis whenever half the bed is a whole number of grid
    // squares - a 200mm bed at 10mm spacing does - and two lines at identical depth z-fight into
    // a dashed mess. Lifting the axes clear of the grid plane is a plain world-space offset, so
    // it behaves the same at every zoom, and 0.05mm is far below anything else in the scene.
    private const float AxisLift = 0.05f;

    private static Element3D AxisLine(SharpDX.Vector3 end, System.Windows.Media.Color color, bool isVisible) {
        var builder = new LineBuilder();
        builder.AddLine(new SharpDX.Vector3(0f, 0f, AxisLift), end);

        return new LineGeometryModel3D {
            Geometry = builder.ToLineGeometry3D(),
            IsHitTestVisible = false,
            Color = color,
            // Heavier than the grid's hairlines so the axes read as annotation rather than as two
            // grid lines that happen to be coloured.
            Thickness = 2.5,
            Visibility = isVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed
        };
    }
}
