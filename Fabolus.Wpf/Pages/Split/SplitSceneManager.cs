using CommunityToolkit.Mvvm.Messaging;
using ControlzEx.Standard;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Wpf.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static Fabolus.Core.Meshes.MeshTools.MeshTools;
using static Fabolus.Wpf.Bolus.BolusStore;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static Fabolus.Core.Meshes.PartingTools.PartingTools;
using Fabolus.Core.Meshes.PartingTools;
using g3;
using Fabolus.Wpf.Features.Mould;

namespace Fabolus.Wpf.Pages.Split;

public class SplitSceneManager : SceneManager {
    private MeshGeometry3D _partingRegion;

    private int _smoothnessDegree = 10;
    private float _model_thickness = 0.15f;
    private Vector3Collection _path = [];

    // parting
    private MeshModel _partingMesh;
    private MeshGeometry3D _partingPathMesh;
    private MeshGeometry3D _offsetMesh;
    private MeshGeometry3D _innerMesh;
    private MeshGeometry3D _mouldMesh;
    private MeshGeometry3D _positivePullMesh;
    private MeshGeometry3D _negativePullMesh;

    // view options
    private SplitViewOptions _view_options;

    public SplitSceneManager() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;

        // request messages
        WeakReferenceMessenger.Default.Register<SplitSceneManager, UpdateSplitViewOptionsMessage>(this, (r, m) => {
            _view_options = m.Options;
            UpdateDisplay();
        });
        WeakReferenceMessenger.Default.Register<SplitSeperationDistanceMessage>(this, (r, m) => _model_thickness = m.Distance);

        // initial values
        UpdateMesh(bolus.TransformedMesh());
        _view_options = WeakReferenceMessenger.Default.Send<SplitRequestViewOptionsMessage>().Response;

        UpdateDisplay();
    }

    private void UpdateDisplay() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        if (bolus is null || bolus.Mesh.IsEmpty()) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        if (bolus is not null && _view_options.ShowBolus) {
            models.Add(new DisplayModel3D {
                Geometry = bolus.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.LightGray,
            });
        }

        // parting curve
        if (_partingPathMesh.Positions.Count > 0 && _view_options.ShowPartingLine) {
            models.Add(new DisplayModel3D {
                Geometry = _partingPathMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });

            // used to show the polylines generated from offsetting the parting curve
            models.Add(new DisplayModel3D {
                Geometry = _offsetMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
            });

            models.Add(new DisplayModel3D {
                Geometry = _innerMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
            });

        }

        if (_partingMesh is not null && _view_options.ShowPartingMesh) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh.ToGeometry(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });
        }

        if (_mouldMesh.Positions.Count > 0 && _view_options.ShowPullRegions) {
            models.Add(new DisplayModel3D {
                Geometry = _mouldMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
                IsTransparent = true,
            });
        }

        double spacing = _view_options.ExplodePartingMeshes ? 15 : 0;
        if (_positivePullMesh is not null && _view_options.ShowNegativeParting) {
            models.Add(new DisplayModel3D {
                Geometry = _positivePullMesh,
                Transform = MeshHelper.TranslationFromAxis(0, -spacing, 0),
                Skin = DiffuseMaterials.Red,
            });
        }
        
        if (_negativePullMesh is not null && _view_options.ShowPositiveParting) {
            models.Add(new DisplayModel3D {
                Geometry = _negativePullMesh,
                Transform = MeshHelper.TranslationFromAxis(0, spacing, 0),
                Skin = DiffuseMaterials.Green,
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

    private void UpdateMesh(MeshModel model) {
        // draft angle meshes
        SetPartingMesh(model);
    }

    private void SetPartingMesh(MeshModel model) {
        // creates the parting mesh to boolean subtract from the main mould
        CuttingMeshParams settings = new() {
            Model = model,
            OuterOffset = 90.0f,
            InnerOffset = 0.5f,
            MeshDepth = 0.2,
            TwistThreshold = 0.40f
        };

        CuttingMeshResults results = PartingTools.DualOffsetCuttingMesh(settings);
        MeshBuilder builder = new();
        builder.AddTube(new Vector3Collection(results.PartingPath.Select(v => ToVector3(v))), 0.3, 16, true);
        _partingPathMesh = builder.ToMeshGeometry3D();

        builder = new();
        builder.AddTube(new Vector3Collection(results.OuterPath.Select(v => ToVector3(v))), 0.3, 16, true);
        _offsetMesh = builder.ToMeshGeometry3D();

        // calculate the offset polyline curve

        builder = new();
        builder.AddTube(new Vector3Collection(results.InnerPath.Select(v => ToVector3(v))), 0.3, 16, true);
        _innerMesh = builder.ToMeshGeometry3D();

        _partingMesh = results.CuttingMesh;

        // create inflated mesh to boolean intersect with the parting mesh
        var mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>().Response;
        _mouldMesh = mould.ToGeometry();

        var boolean_response = MeshTools.BooleanSubtraction(mould, _partingMesh);
        if (boolean_response.IsFailure || boolean_response.Data is null) {
            var errors = boolean_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Mould Splitting Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // break the models up into connected components
        var models = MeshTools.SeperateModels(boolean_response.Data);

        // check if the models are valid after split
        switch (models.Length) {
            case (< 1):
                MessageBox.Show("No models found after boolean subtraction.", "Split Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            case (1):
                _positivePullMesh = models[0].ToGeometry();
                _negativePullMesh = null; // empty model for negative part
                break;
            case (2):
                var bounds0 = models[0].BoundsLower();
                var bounds1 = models[1].BoundsLower();

                if (bounds0[1] < bounds1[1]) {
                    _positivePullMesh = models[0].ToGeometry(); // if the y coordinate of the first mesh is lower than the second, then it is the positive part
                    _negativePullMesh = models[1].ToGeometry();
                } else {
                    _positivePullMesh = models[1].ToGeometry(); // if the y coordinate of the first mesh is lower than the second, then it is the positive part
                    _negativePullMesh = models[0].ToGeometry();
                }
                break;
            default:
                MessageBox.Show("More than two models found after boolean subtraction. Please check the model.", "Split Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
        }

    }

    private static Vector3 ToVector3(System.Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);
    
}




