using Fabolus.Core;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common.Extensions;
using SharpDX;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Common.Bolus;

public class BolusModel : Fabolus.Core.BolusModel.Bolus {

    //Mesh is the raw structure, geometry is the transforms applied
    public BolusType BolusType { get; set; } = BolusType.Raw;
    public MeshGeometry3D Geometry { get; set; }
    public BolusTransform Transform { get; set; } = new();
    public Vector3 TranslateOffset { get; set; } = Vector3.Zero;
    public string? Filepath { get; init; } = null;

    #region Constructors

    public BolusModel() {
        Mesh = new();
        Geometry = new();
        Transform = new();
    }

    public BolusModel(MeshModel mesh) : base(mesh) {
        Geometry = mesh.ToGeometry();
        Transform = new();
    }

    public BolusModel(Fabolus.Core.BolusModel.Bolus bolus) : base(bolus.Mesh) {
        Geometry = bolus.Mesh.ToGeometry();
        Transform = new();
    }

    #endregion

    #region Public Methods

    public void ApplyTransform(BolusTransform transform) {
        Transform = transform;
        Geometry = Transform.ApplyTransforms(Mesh).ToGeometry();
    }

    public MeshModel TransformedMesh() =>
        Transform.ApplyTransforms(Mesh);

    public static bool IsNullOrEmpty(BolusModel? bolus) =>
        bolus is null || bolus.Mesh.IsEmpty() || bolus.Geometry.IsEmpty();

    #endregion
}
