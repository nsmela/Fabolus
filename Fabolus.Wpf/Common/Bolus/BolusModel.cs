using Fabolus.Core;
using g3;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using SharpDX;

namespace Fabolus.Wpf.Common.Bolus;

public class BolusModel : Fabolus.Core.BolusModel.Bolus {

    //Mesh is the raw structure, geometry is the transforms applied
    public BolusType BolusType { get; set; } = BolusType.Raw;
    public MeshGeometry3D Geometry { get; set; }
    public BolusTransform Transform { get; set; } = new();
    public Vector3 TranslateOffset { get; set; } = Vector3.Zero;

    #region Constructors
    public BolusModel() {
        Mesh = new();
        Geometry = new();
        Transform = new();
    }

    public BolusModel(DMesh3 mesh) {
        SetMesh(mesh);
    }

    public BolusModel(MeshGeometry3D geometry) {
        SetGeometry(geometry);
    }

    public BolusModel(Fabolus.Core.BolusModel.Bolus bolus) {
        SetMesh(bolus.Mesh);
    }

    #endregion

    #region Public Methods

    public void ApplyTransform(BolusTransform transform) {
        Transform = transform;
        Geometry = Transform.ApplyTransforms(Mesh);
    }

    public bool IsLoaded =>
        Mesh is not null &&
        Mesh.VertexCount > 0 &&
        Geometry is not null &&
        Geometry.Positions.Count > 0;

    public void SetMesh(DMesh3 mesh) {
        Mesh = mesh;
        Geometry = mesh.ToGeometry();
        Transform = new();
    }

    public void SetGeometry(MeshGeometry3D geometry) {
        Geometry = geometry;
        Mesh = geometry.ToDMesh();
        Transform = new();
    }

    #endregion
}
