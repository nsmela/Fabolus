using Fabolus.Core;
using Fabolus.Wpf.Common.Extensions;
using SharpDX;
using DMesh3 = g3.DMesh3;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

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

    public BolusModel(DMesh3 mesh) : base(mesh) {
        Geometry = mesh.ToGeometry();
        Transform = new();
    }

    public BolusModel(Fabolus.Core.BolusModel.Bolus bolus) : base(bolus.Mesh) {
        Geometry = bolus.Mesh.ToGeometry();
        CopyOffsets(bolus);
        Transform = new();
    }

    #endregion

    #region Public Methods

    public void ApplyTransform(BolusTransform transform) {
        Transform = transform;
        Geometry = Transform.ApplyTransforms(Mesh).ToGeometry();
    }

    public DMesh3 TransformedMesh => Transform.ApplyTransforms(Mesh);

    public bool IsLoaded =>
        Mesh.IsEmpty() &&
        Geometry is not null &&
        Geometry.Positions.Count > 0;

    public bool IsValid() {
        if (Geometry is null || Geometry.TriangleIndices.Count() == 0) { return false; }

        return true;
    }

    public bool IsNotValid() => !IsValid();


    #endregion
}
