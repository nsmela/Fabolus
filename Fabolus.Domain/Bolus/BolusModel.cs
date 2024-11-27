using g3;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Core.Bolus;
public class BolusModel {
    public DMesh3 Mesh { get; set; } 
    public MeshGeometry3D Geometry { get; set; }

    #region Constructors
    public BolusModel()
    {
        Mesh = new DMesh3();
        Geometry = new MeshGeometry3D();
    }

    public BolusModel(DMesh3 mesh)
    {
        Mesh = mesh;
        Geometry = mesh.ToGeometry();
    }

    public BolusModel(MeshGeometry3D geometry) {
        Geometry = geometry;
        Mesh = geometry.ToDMesh();
    }

    #endregion

    #region Public Methods

    public bool IsLoaded => 
        Mesh is not null && 
        Mesh.VertexCount > 0 &&
        Geometry is not null &&
        Geometry.Positions.Count > 0;

    #endregion
}
