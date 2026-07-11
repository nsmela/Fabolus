
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
                new Vector3(minX + spacing * i, minY, 0),
                new Vector3(minX + spacing * i, maxY, 0));
        }

        for (int i = 0; i <= depth / spacing; i++) {
            grid.AddLine(
                new Vector3(minX, minY + spacing * i, 0),
                new Vector3(maxX, minY + spacing * i, 0));
        }

        return new LineGeometryModel3D {
            Geometry = grid.ToLineGeometry3D(),
            IsHitTestVisible = false,
            Visibility = isVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed
        };
    }
}
