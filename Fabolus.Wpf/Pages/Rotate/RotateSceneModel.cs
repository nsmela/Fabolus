using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using SharpDX;
using Fabolus.Wpf.Common.Mesh;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Colors = System.Windows.Media.Colors;

namespace Fabolus.Wpf.Pages.Rotate;
public sealed class RotateSceneModel : SceneModel {
    private Material _overhangSkin = new ColorStripeMaterial();

    public RotateSceneModel() : base() {
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial();
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([]));
            return;
        }

        var axis = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
        var refAxis = bolus.Transform.Transform(axis).ToVector3();

        bolus.Geometry.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(bolus.Geometry, refAxis);

        var display = new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = bolus.Transform,
            Skin = _overhangSkin
        };

        //testing, to see how the ref angle is being managed
        var mesh = new MeshBuilder();
        mesh.AddArrow(Vector3.Zero, refAxis, 3.0, 16);

        var refArrow = new DisplayModel3D {
            Geometry = mesh.ToMeshGeometry3D(),
            Skin = PhongMaterials.Blue,
            Transform = MeshHelper.TransformEmpty
        };

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([display, refArrow]));
    }
}
