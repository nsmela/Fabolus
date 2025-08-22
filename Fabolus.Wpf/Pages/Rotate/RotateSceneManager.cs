using CommunityToolkit.Mvvm.Messaging;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using static Fabolus.Wpf.Bolus.BolusStore;

// aliases
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using Transform3D = System.Windows.Media.Media3D.Transform3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed class RotateSceneManager : SceneManagerBase {
    public const float DEFAULT_OVERHANG_LOWER = 50.0f;
    public const float DEFAULT_OVERHANG_UPPER = 70.0f;

    private float _overhangLowerAngle = DEFAULT_OVERHANG_LOWER;
    private float _overhangUpperAngle = DEFAULT_OVERHANG_UPPER;
    private Vector3D _overhangAxis = new Vector3D(0, 0, -1);
    private Material _overhangSkin = new ColorStripeMaterial();
    private Vector3 _tempAxis = Vector3.Zero;
    private float _tempAngle = 0.0f;
    private Vector3 _activeAxis = Vector3.Zero; // to display the proper widget

    private Transform3D TempRotation => MeshHelper.TransformFromAxis(_tempAxis, _tempAngle);

    private DisplayModel3D[] AxisLines { get; set; } = [];

    protected override void RegisterMessages() {
        _messenger.Register<ApplyTempRotationMessage>(this, (r, m) => ApplyTempRotation(m.Axis, m.Angle));
        _messenger.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated(m.Bolus));
        _messenger.Register<ApplyOverhangSettings>(this, (r, m) => ApplyOverhangSettings(m.LowerAngle, m.UpperAngle));
        _messenger.Register<ShowActiveRotationMessage>(this, (r, m) => UpdateActiveAxis(m.axis));
    }

    public RotateSceneManager() {
        RegisterMessages();

        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial(_overhangLowerAngle, _overhangUpperAngle);
        var printbedWidth = _messenger.Send<PreferencesPrintbedWidthRequest>().Response;
        var printbedDepth = _messenger.Send<PreferencesPrintbedDepthRequest>().Response;
        AxisLines = GenerateAxisLines(printbedWidth / 2, printbedDepth / 2);

        var bolus = _messenger.Send(new BolusRequestMessage());
        BolusUpdated(bolus);
    }

    private void ApplyOverhangSettings(float lower, float upper) {
        _overhangLowerAngle = lower;
        _overhangUpperAngle = upper;
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial(_overhangLowerAngle, _overhangUpperAngle);

        var bolus = _messenger.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void ApplyTempRotation(Vector3 axis, float angle) {
        _tempAxis = axis;
        _tempAngle = angle;

        var bolus = _messenger.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void UpdateActiveAxis(Vector3 axis) {
        _activeAxis = axis;
        var bolus = _messenger.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void BolusUpdated(BolusModel? bolus) {
        _tempAxis = Vector3.Zero;
        _tempAngle = 0.0f;

        UpdateDisplay(bolus);
    }

    void UpdateDisplay(BolusModel? bolus) {
        if (BolusModel.IsNullOrEmpty(bolus)) {
            _messenger.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        //overhangs rely on the normals of the mesh
        //temp rotations ened to be calculated with the oposite angle

        var refAxis = MeshHelper.TransformFromAxis(_tempAxis, -_tempAngle).Transform(_overhangAxis).ToVector3();

        bolus!.Geometry.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(bolus.Geometry, refAxis);

        var models = new List<DisplayModel3D>();
        models.Add(new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = TempRotation,
            Skin = _overhangSkin
        });

        if (_activeAxis != Vector3.Zero) {
            models.Add(GenerateAxisWidget(bolus.Geometry.BoundingSphere.Radius));
        }

        foreach (var model in AxisLines) { 
            models.Add(model); 
        }

        _messenger.Send(new MeshDisplayUpdatedMessage(models));
    }

    private DisplayModel3D GenerateAxisWidget(float radius) {
        var mesh = new MeshBuilder();
        mesh.AddTorus(radius * 2, 2.0);

        Transform3D transform = _activeAxis switch {
            { X: 1 } => MeshHelper.TransformFromAxis(Vector3.UnitY, 90.0f),
            { Y: 1 } => MeshHelper.TransformFromAxis(Vector3.UnitX, 90.0f),
            _ => MeshHelper.TransformEmpty,
        };

        Material skin = _activeAxis switch {
            { X: 1 } => DiffuseMaterials.Red,
            { Y: 1 } => DiffuseMaterials.Green,
            _ => DiffuseMaterials.Blue,
        };

        return new DisplayModel3D {
            Geometry = mesh.ToMesh(),
            Transform = transform,
            Skin = skin,
        };

    }

    private DisplayModel3D[] GenerateAxisLines(float xSize, float ySize) {
        var models = new List<DisplayModel3D>();

        var builder = new MeshBuilder();
        builder.AddCylinder(new Vector3(-xSize, 0, 0), new Vector3(xSize, 0, 0), 0.75f, 32, true, true);
        models.Add(new DisplayModel3D {
            Geometry = builder.ToMesh(),
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Red,
        });

        var yBuilder = new MeshBuilder();
        yBuilder.AddCylinder(new Vector3(0, -ySize, 0), new Vector3(0, ySize, 0), 0.75f, 32, true, true);
        models.Add(new DisplayModel3D {
            Geometry = yBuilder.ToMesh(),
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Green,
        });

        return models.ToArray();
    }

}