using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Core.Meshes.PartingTools;
using Fabolus.Core.Meshes.MeshTools;

namespace Fabolus.Wpf.Pages.Split;

public class SplitSceneManager : SceneManager {

    // parting
    private CuttingMeshParams _settings = new();
    private MeshGeometry3D _bolus = new();
    private MeshGeometry3D _partingMesh = new();
    private MeshGeometry3D _partingPathMesh = new();
    private MeshGeometry3D _mouldMesh = new();
    private MeshGeometry3D _positivePullMesh;
    private MeshGeometry3D _negativePullMesh;

    private MeshGeometry3D _positiveRegion = new();
    private MeshGeometry3D _negativeRegion = new();
    private MeshGeometry3D _neutralRegion = new();

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
        UpdateRegions(bolus.TransformedMesh());

        _settings.Model = bolus.TransformedMesh();
        _view_options = WeakReferenceMessenger.Default.Send<SplitRequestViewOptionsMessage>().Response;

        var results = WeakReferenceMessenger.Default.Send(new SplitRequestResultsMessage()).Response;
        UpdateResults(results);
    }

    private void UpdateRegions(MeshModel bolus) {
        var results = DraftRegions.GenerateDraftMeshes(bolus, System.Numerics.Vector3.UnitY, 5.0);

        _positiveRegion = results[DraftRegions.DraftRegionClassification.Positive].ToGeometry();
        _negativeRegion = results[DraftRegions.DraftRegionClassification.Negative].ToGeometry();
        _neutralRegion = results[DraftRegions.DraftRegionClassification.Neutral].ToGeometry();

    }

    private void UpdateResults(CuttingMeshResults results) {
        _bolus = results.Model.ToGeometry();

        List<Vector3> path = [];
        foreach(var p in results.PartingPaths) {
            path.AddRange(p.Select(pp => new Vector3(pp.X, pp.Y, pp.Z)));
        }
        MeshBuilder builder = new();
        foreach (Vector3 v in path.Select(v => new Vector3(v.X, v.Y, v.Z))) {
            builder.AddSphere(v, 0.25);
        }
        _partingPathMesh = builder.ToMeshGeometry3D();

        _partingMesh = results.CuttingMesh.ToGeometry();
        _mouldMesh = results.Mould is not null ? results.Mould.ToGeometry() : new();

        _negativePullMesh = MeshModel.IsNullOrEmpty(results.NegativePullMesh)
            ? new()
            : results.NegativePullMesh.ToGeometry();
        _positivePullMesh = MeshModel.IsNullOrEmpty(results.PositivePullMesh) 
            ? new() 
            : results.PositivePullMesh.ToGeometry();

        UpdateDisplay();
    }

    private void UpdateDisplay() {
        var models = new List<DisplayModel3D>();

        // show bolus
        if (_view_options.ShowBolus) {
            models.Add(new DisplayModel3D {
                Geometry = _positiveRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Red,
            });

            models.Add(new DisplayModel3D {
                Geometry = _negativeRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Green,
            });

            models.Add(new DisplayModel3D {
                Geometry = _neutralRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.LightGray,
            });
        }

        // parting line
        if (_view_options.ShowPartingLine) {
            models.Add(new DisplayModel3D {
                Geometry = _partingPathMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });
        }

        // parting mesh
        if (_partingMesh is not null && _view_options.ShowPartingMesh) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });
        }

        // mould mesh
        if (_view_options.ShowPullRegions && _mouldMesh is not null && !_mouldMesh.IsEmpty()) {
            models.Add(new DisplayModel3D {
                Geometry = _mouldMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
                IsTransparent = true,
            });
        }

        // final split mould display
        double spacing = _view_options.ExplodePartingMeshes ? 15 : 0;
        if (_positivePullMesh is not null && _view_options.ShowPositiveParting) {
            models.Add(new DisplayModel3D {
                Geometry = _positivePullMesh,
                Transform = MeshHelper.TranslationFromAxis(0, spacing, 0),
                Skin = DiffuseMaterials.Red,
            });
        }
        
        if (_negativePullMesh is not null && _view_options.ShowNegativeParting) {
            models.Add(new DisplayModel3D {
                Geometry = _negativePullMesh,
                Transform = MeshHelper.TranslationFromAxis(0, -spacing, 0),
                Skin = DiffuseMaterials.Green,
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }
    

    private static Vector3 ToVector3(System.Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);

}




