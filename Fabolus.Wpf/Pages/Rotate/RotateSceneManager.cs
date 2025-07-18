﻿using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using static Fabolus.Wpf.Bolus.BolusStore;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using Transform3D = System.Windows.Media.Media3D.Transform3D;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed class RotateSceneManager : SceneManager {
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

    public RotateSceneManager() {
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial(_overhangLowerAngle, _overhangUpperAngle);
        AxisLines = GenerateAxisLines(100, 100);

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<ApplyTempRotationMessage>(this, (r, m) => ApplyTempRotation(m.Axis, m.Angle));
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated(m.Bolus));
        WeakReferenceMessenger.Default.Register<ApplyOverhangSettings>(this, (r, m) => ApplyOverhangSettings(m.LowerAngle, m.UpperAngle));

        WeakReferenceMessenger.Default.Register<ShowActiveRotationMessage>(this, (r, m) => UpdateActiveAxis(m.axis));

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

    private void UpdateActiveAxis(Vector3 axis) {
        _activeAxis = axis;
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
    }

    private void BolusUpdated(BolusModel? bolus) {
        _tempAxis = Vector3.Zero;
        _tempAngle = 0.0f;

        UpdateDisplay(bolus);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (BolusModel.IsNullOrEmpty(bolus)) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
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

        foreach (var model in AxisLines) { models.Add(model); }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
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