using g3;
using System.Windows.Media.Media3D;

namespace Fabolus.Core.Bolus;
public class BolusModel {
    public DMesh3 Mesh { get; set; } = new DMesh3();
    public MeshGeometry3D Geometry { get; set; } = new MeshGeometry3D();
}
