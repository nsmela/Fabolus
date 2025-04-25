
namespace Fabolus.Wpf.Common.Mesh;
public record struct DisplayModel3D(
    HelixToolkit.Wpf.SharpDX.MeshGeometry3D Geometry, 
    HelixToolkit.Wpf.SharpDX.Material Skin, 
    System.Windows.Media.Media3D.Transform3D Transform,
    bool Cull = false);
