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
using System.Windows;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Core.Meshes.PartingTools;
using Fabolus.Wpf.Features.Mould;

namespace Fabolus.Wpf.Pages.Split;

public class SplitSceneManager : SceneManager {
    private MeshGeometry3D _partingRegion;

    private int _smoothnessDegree = 10;
    private float _model_thickness = 0.15f;
    private Vector3Collection _path = [];
    private int[] _path_indices = [];

    // parting
    private CuttingMeshParams _settings = new();
    private MeshGeometry3D _partingMesh = new();
    private MeshGeometry3D _partingPathMesh = new();
    private MeshGeometry3D _boundryMesh;
    private MeshGeometry3D _exteriorPartingMesh;
    private MeshGeometry3D _outerMesh;
    private MeshGeometry3D _concaveMesh = new();
    private MeshGeometry3D _innerMesh;
    private MeshGeometry3D _mouldMesh = new();
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
        WeakReferenceMessenger.Default.Register<SplitSceneManager, SplitResultsMessage>(this, (r,m) => {
            UpdateResults(m.Results);
        });

        // initial values
        _settings.Model = bolus.TransformedMesh();
        _view_options = WeakReferenceMessenger.Default.Send<SplitRequestViewOptionsMessage>().Response;

        var results = WeakReferenceMessenger.Default.Send(new SplitRequestResultsMessage()).Response;
        UpdateResults(results);

        UpdateDisplay();
    }

    private void UpdateResults(CuttingMeshResults results) {
        _path_indices = results.PartingIndices;
        _partingMesh = results.CuttingMesh.ToGeometry();
        _mouldMesh = results.Mould is not null ? results.Mould.ToGeometry() : new();

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
        if (_view_options.ShowPartingLine) {
            models.Add(new DisplayModel3D {
                Geometry = _partingPathMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });
        }

        if (_partingMesh is not null && _view_options.ShowPartingMesh) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });

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
    }

    private static Vector3 ToVector3(System.Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);
    private static Vector3Collection ToVectorCollection(IEnumerable<System.Numerics.Vector3> vectors) => new Vector3Collection(vectors.Select(ToVector3));
    private static System.Numerics.Vector3[] ToGenericVectorArray(IEnumerable<Vector3> vectors) =>
        vectors.Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)).ToArray();
}




