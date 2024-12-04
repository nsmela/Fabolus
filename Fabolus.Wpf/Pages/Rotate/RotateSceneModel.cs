using Fabolus.Core.Bolus;
using Fabolus.Core.Common;
using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;

using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using SharpDX;
using Fabolus.Wpf.Common.Mesh;
using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Pages.Rotate;
public sealed class RotateSceneModel : SceneModel {
    private Material _overhangsMaterial = new ColorStripeMaterial();

    public RotateSceneModel() : base() {
        _overhangsMaterial = OverhangsHelper.CreateOverhangsMaterial();
        WeakReferenceMessenger.Default.Send(new MeshMaterialsMessage([_overhangsMaterial])); //TODO: a method to change this material when settings change
    }

    protected override void UpdateModel(BolusModel? bolus) {
        if (bolus is null) {
            _model = null;
            _transform = MeshHelper.TransformEmpty.ToMatrix();
            UpdateScene();
            return;
        }

        _model = bolus.Geometry;
        _model.UpdateOctree();
        _model.UpdateBounds();
        _transform = bolus.TransformMatrix;
        _model.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(_model, new Vector3(0, 0, 1));

        UpdateScene();
    }
}
