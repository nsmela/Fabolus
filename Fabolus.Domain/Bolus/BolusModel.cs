using g3;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Core.Bolus;
public class BolusModel {
    public DMesh3 Mesh { get; set; } 
    public MeshGeometry3D Geometry { get; set; }
    public Matrix TransformMatrix { get; set; }

    #region Constructors
    public BolusModel()
    {
        Mesh = new DMesh3();
        Geometry = new MeshGeometry3D();
        TransformMatrix = new Matrix(1.0f);
    }

    public BolusModel(DMesh3 mesh)
    {
        Mesh = mesh;
        Geometry = mesh.ToGeometry();
        TransformMatrix = new Matrix(1.0f);
    }

    public BolusModel(MeshGeometry3D geometry) {
        Geometry = geometry;
        Mesh = geometry.ToDMesh();
        TransformMatrix = new Matrix(1.0f);
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
