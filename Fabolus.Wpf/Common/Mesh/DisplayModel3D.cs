
namespace Fabolus.Wpf.Common.Mesh;
public record struct DisplayModel3D(
    HelixToolkit.Wpf.SharpDX.MeshGeometry3D Geometry, 
    HelixToolkit.Wpf.SharpDX.Material Skin, 
    System.Windows.Media.Media3D.Transform3D Transform,
    bool IsTransparent = false,
    bool ShowWireframe = false
    ) {

    public static bool IsValid(DisplayModel3D? model) =>
        model is not null
        && model.Value.Geometry is not null
        && model.Value.Geometry.Positions is not null
        && model.Value.Geometry.Positions.Count > 0;
}
