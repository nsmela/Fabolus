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
    private float _overhangLowerAngle;
    private float _overhangUpperAngle;
    private Vector3D _overhangAxis = new Vector3D(0, 0, -1);
    private Material _overhangSkin = new ColorStripeMaterial();
    private Vector3 _tempAxis = Vector3.Zero;
    private float _tempAngle = 0.0f;

    private Transform3D TempRotation => MeshHelper.TransformFromAxis(_tempAxis, _tempAngle);

    public RotateSceneManager() {
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial();

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<ApplyTempRotationMessage>(this, (r, m) => ApplyTempRotation(m.Axis, m.Angle));
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated(m.bolus));
        WeakReferenceMessenger.Default.Register<ApplyOverhangSettings>(this, (r, m) => ApplyOverhangSettings(m.LowerAngle, m.UpperAngle));

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        BolusUpdated(bolus);
    }

    private void ApplyOverhangSettings(float lower, float upper) {
        _overhangLowerAngle = lower;
        _overhangUpperAngle = upper;
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial(_overhangLowerAngle, _overhangUpperAngle);

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void ApplyTempRotation(Vector3 axis, float angle) {
        _tempAxis = axis;
        _tempAngle = angle;

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void BolusUpdated(BolusModel? bolus) {
        _tempAxis = Vector3.Zero;
        _tempAngle = 0.0f;

        UpdateDisplay(bolus);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([]));
            return;
        }

        //overhangs rely on the normals of the mesh
        //temp rotations ened to be calculated with the oposite angle

        var refAxis = MeshHelper.TransformFromAxis(_tempAxis, -_tempAngle).Transform(_overhangAxis).ToVector3();

        bolus.Geometry.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(bolus.Geometry, refAxis);

        var models = new List<DisplayModel3D>();
        models.Add( new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = TempRotation,
            Skin = _overhangSkin
        });

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge(models));
    }
}
