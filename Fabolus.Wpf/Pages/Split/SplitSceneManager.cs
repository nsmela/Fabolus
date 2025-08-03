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
using System.IO;

namespace Fabolus.Wpf.Pages.Split;

public class SplitSceneManager : SceneManager {
    private MeshGeometry3D _partingRegion;

    private int _smoothnessDegree = 10;
    private float _model_thickness = 0.15f;
    private Vector3Collection _path = [];
    private int[] _path_indices = [];

    // parting
    private CuttingMeshParams _settings = new();
    private MeshModel _partingMesh;
    private MeshGeometry3D _partingPathMesh;
    private MeshGeometry3D _boundryMesh;
    private MeshGeometry3D _exteriorPartingMesh;
    private MeshGeometry3D _outerMesh;
    private MeshGeometry3D _concaveMesh = new();
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
        WeakReferenceMessenger.Default.Register<SplitSceneManager, SplitSettingsMessage>(this, (r, m) => UpdateSettings(m.Settings));

        // initial values
        _settings.Model = bolus.TransformedMesh();
        _view_options = WeakReferenceMessenger.Default.Send<SplitRequestViewOptionsMessage>().Response;

        _path_indices = PartingTools.GeneratePartingLine(_settings.Model).ToArray();
        _path = new Vector3Collection(_settings.Model.GetVertices(_path_indices).Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])));
        MeshBuilder builder = new();
        builder.AddTube(_path, 0.3, 16, true);
        _partingPathMesh = builder.ToMeshGeometry3D();

        var settings = WeakReferenceMessenger.Default.Send<SplitRequestSettingsMessage>().Response;
        UpdateSettings(settings);
        UpdateDisplay();
    }

    /// <summary>
    /// Generate the parting mesh step-by-step for troubleshooting
    /// </summary>
    /// <param name="settings"></param>
    private void UpdateSettings(CuttingMeshParams settings) {
        _settings = settings with { Model = _settings.Model };

        var results = PartingTools.GeneratePartingMesh(_settings.Model, _path_indices, _settings.InnerOffset, _settings.OuterOffset);
        _partingMesh = results.Data;

        UpdateDisplay();
        return; //TODO: remove early return

        var mould = WeakReferenceMessenger.Default.Send(new MouldRequestMessage()).Response;
        if (mould.Mesh is null || mould.IsEmpty()) {
            MessageBox.Show( "A valid mould is required to split", "Triangulate error", MessageBoxButton.OK, MessageBoxImage.Error);

            UpdateDisplay();
            return;
        }

        var response = MeshTools.BooleanSubtraction(mould, _partingMesh);

        if (response.IsFailure || response.Data is null) {
            var errors = response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Triangulate Split Mesh Error", MessageBoxButton.OK, MessageBoxImage.Error);

            UpdateDisplay();
            return;
        }

        _mouldMesh = response.Data.ToGeometry();

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

        // testing concave detection
        if (_concaveMesh is not null && _concaveMesh.Positions is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _concaveMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Emerald,
            });
        }

        // parting curve
        if (false && _partingPathMesh.Positions.Count > 0 && _view_options.ShowPartingLine) {
            models.Add(new DisplayModel3D {
                Geometry = _partingPathMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });



            models.Add(new DisplayModel3D {
                Geometry = _innerMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });

        }

        if (_partingMesh is not null && _view_options.ShowPartingMesh) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh.ToGeometry(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });

            //models.Add(new DisplayModel3D {
            //    Geometry = _exteriorPartingMesh,
            //    Transform = MeshHelper.TransformEmpty,
            //    Skin = DiffuseMaterials.Violet,
            //});
        }

        if (_mouldMesh is not null && _mouldMesh.Positions is not null && _mouldMesh.Positions.Count > 0 && _view_options.ShowPullRegions) {
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

    private static Vector3 ToVector3(System.Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);
    private static Vector3Collection ToVectorCollection(IEnumerable<System.Numerics.Vector3> vectors ) => new Vector3Collection(vectors.Select(ToVector3));
    private static System.Numerics.Vector3[] ToGenericVectorArray(IEnumerable<Vector3> vectors) =>
        vectors.Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)).ToArray();
}




