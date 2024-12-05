using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using static Fabolus.Wpf.Bolus.BolusStore;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using Colors = System.Windows.Media.Colors;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using Transform3D = System.Windows.Media.Media3D.Transform3D;
using Transform3DGroup = System.Windows.Media.Media3D.Transform3DGroup;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed class RotateSceneManager : SceneManager {
    private Vector3D _overhangAxis = new Vector3D(0, 0, -1);
    private Material _overhangSkin = new ColorStripeMaterial();
    private Transform3D _tempRotation = MeshHelper.TransformEmpty;

    public RotateSceneManager() {
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial();

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<ApplyTempRotationMessage>(this, (r, m) => ApplyTempRotation(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated(m.bolus));
    }

    private void ApplyTempRotation(Vector3D axis, float angle) {
        _tempRotation = MeshHelper.TransformFromAxis(axis, angle);
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void BolusUpdated(BolusModel? bolus) {
        _tempRotation = MeshHelper.TransformEmpty;
        UpdateDisplay(bolus);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([]));
            return;
        }

        //overhangs rely on the normals of the mesh
        //temp rotations ened to be calculated with the oposite angle

        var refAxis = _tempRotation.Transform(_overhangAxis).ToVector3();

        bolus.Geometry.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(bolus.Geometry, refAxis);

        var models = new List<DisplayModel3D>();
        models.Add( new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = _tempRotation,
            Skin = _overhangSkin
        });

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge(models));
    }
}
