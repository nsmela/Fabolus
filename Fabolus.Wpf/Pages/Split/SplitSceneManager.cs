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

namespace Fabolus.Wpf.Pages.Split;

public class SplitSceneManager : SceneManager {
    private MeshGeometry3D? _previewMesh;
    private Material _previewSkin = DiffuseMaterials.Turquoise;

    private Guid? BolusId;

    private Guid? PartingRegionId;
    private float _model_thickness = 3.0f;
    private MeshModel _partingMeshModel;
    private MeshModel _partPositiveModel;
    private MeshModel _partNegativeModel;
    private MeshGeometry3D _partingRegion;
    private Material _partingMaterial = DiffuseMaterials.Green;

    private int _smoothnessDegree = 10;
    private Vector3Collection _path = [];

    private MeshGeometry3D? _draftAngleMeshPositive;
    private MeshGeometry3D? _draftAngleMeshNegative;
    private MeshGeometry3D? _draftAngleMeshNeutral;
    private const double DRAFT_ANGLE_THRESHOLD_DEGREES = 10.0;
    private double[] _draftPullDirection = new double[3] { 0, 1, 0 }; // pulling in the positive Y direction

    // parting
    private Vector3Collection _parting_curve = [];
    private Vector3Collection _contour_curve = [];
    private MeshModel _partingMesh;

    public SplitSceneManager() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        BolusId = bolus?.Geometry?.GUID;

        // request messages
        WeakReferenceMessenger.Default.Register<SplitSceneManager, SplitRequestModels>(this, (r,m) => m.Reply([r._partNegativeModel, r._partPositiveModel]));

        WeakReferenceMessenger.Default.Register<SplitSeperationDistanceMessage>(this, (r, m) => _model_thickness = m.Distance);
        UpdateMesh(bolus.TransformedMesh());
    }

    protected override void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) {
            UpdateDisplay();
            return;
        }

        var mouse = args as MouseEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
            || mouse.MiddleButton == MouseButtonState.Pressed
            || mouse.LeftButton == MouseButtonState.Pressed) {
            UpdateDisplay();
            return;
        }

        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        SetPreview(bolusHit);

    }

    protected override void OnMouseDown(List<HitTestResult> hits, InputEventArgs args) {
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) {
            UpdateDisplay();
            return;
        }

        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == PartingRegionId);
        if (bolusHit is null) { return; }
        double[] start = { bolusHit.PointHit.X, bolusHit.PointHit.Y, bolusHit.PointHit.Z };
        double[] end = { 0, 0, 0 };
    }

    private void SetPreview(HitTestResult? hit) {
        if (hit is null) {
            UpdateDisplay();
            return;
        }

        MeshBuilder builder = new MeshBuilder();
        builder.AddSphere(hit.PointHit, 0.5f);
        _previewMesh = builder.ToMeshGeometry3D();

        UpdateDisplay();
    }

    private void UpdateDisplay() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        if (bolus is null || bolus.Mesh.IsEmpty()) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        // show draft angle results
        if (_draftAngleMeshPositive is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _draftAngleMeshPositive,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Green,
            });
        }
        
        if (_draftAngleMeshNegative is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _draftAngleMeshNegative,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Red,
            });
        }
        
        if (_draftAngleMeshNeutral is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _draftAngleMeshNeutral,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Gray,
            });
        }

        // parting curve
        if (_parting_curve.Count > 0) {
            MeshBuilder builder = new();
            builder.AddTube(_parting_curve, 0.3, 16, true);
            models.Add(new DisplayModel3D {
                Geometry = builder.ToMeshGeometry3D(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });
        }

        // contour curve (used to cut the mould)
        if (_contour_curve.Count > 0) {
            MeshBuilder builder = new();
            builder.AddTube(_contour_curve, 0.3, 16, true);
            models.Add(new DisplayModel3D {
                Geometry = builder.ToMeshGeometry3D(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Copper,
            });
        }

        if (_previewMesh is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _previewMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = _previewSkin,
            });
        }

        if(_partingMesh is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh.ToGeometry(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });
        }

        //if (_partNegativeModel is not null) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = _partNegativeModel.ToGeometry(),
        //        Transform = MeshHelper.TranslationFromAxis(0, -15, 0),
        //        Skin = DiffuseMaterials.Red,
        //    });
        //}
        //
        //if (_partPositiveModel is not null) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = _partPositiveModel.ToGeometry(),
        //        Transform = MeshHelper.TranslationFromAxis(0, 15, 0),
        //        Skin = PhongMaterials.Blue,
        //    });
        //}

        if (_path.Count > 0) {
            MeshBuilder builder = new();
            builder.AddTube(_path, 0.2, 16, false);
            models.Add(new DisplayModel3D {
                Geometry = builder.ToMeshGeometry3D(),
                Transform = MeshHelper.TransformEmpty,
                Skin = PhongMaterials.Black,
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

    private void UpdateMesh(MeshModel model) {
        _partingMeshModel = MeshTools.PartingRegion(model, _smoothnessDegree);
        _partingRegion = _partingMeshModel.ToGeometry();
        PartingRegionId = _partingRegion.GUID;

        // draft angle meshes
        SetDraftMeshes(model);
    }

    private void SetDraftMeshes(MeshModel model) {
        // draft angle meshes
        DraftCollection results = GenerateDraftCollection(model, System.Numerics.Vector3.UnitY, DRAFT_ANGLE_THRESHOLD_DEGREES);

        Vector3 v0, v1, v2; // to be used in the loop
        double[] values = new double[9];

        MeshBuilder positive_mesh = new();
        MeshBuilder negative_mesh = new();
        MeshBuilder neutral_mesh = new();
        MeshBuilder invalid_mesh = new();

        // add triangles to meshes
        foreach (var (tId, result) in results) {
            values = model.GetTriangleAsDoubles(tId);
            v0 = new Vector3((float)values[0], (float)values[1], (float)values[2]);
            v1 = new Vector3((float)values[3], (float)values[4], (float)values[5]);
            v2 = new Vector3((float)values[6], (float)values[7], (float)values[8]);

            if (result == DraftClassification.POSITIVE) { positive_mesh.AddTriangle(v0, v1, v2); }
            if (result == DraftClassification.NEGATIVE) { negative_mesh.AddTriangle(v0, v1, v2); }
            if (result == DraftClassification.NEUTRAL) { neutral_mesh.AddTriangle(v0, v1, v2); }
        }

        _draftAngleMeshPositive = positive_mesh.ToMeshGeometry3D();
        _draftAngleMeshNegative = negative_mesh.ToMeshGeometry3D();
        _draftAngleMeshNeutral = neutral_mesh.ToMeshGeometry3D();

        // parting line
        // find edges for parting line and smooth that path
        var path_response = PartingLine(model, results);

        if (path_response.IsFailure || path_response.Data is null)
        {
            var errors = path_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Generate Parting Line Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var path = path_response.Data.Select(v => new Vector3(v.X, v.Y, v.Z)); // converting System.Numerics.Vector3[] to SharpDX.Vector3 IEnumerable
        _parting_curve = new Vector3Collection(path);

        // TODO: temp, showing the curve generated to cut the mesh
        var contour_response = PartingTools.PartingMesh(_parting_curve.Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)), 20);
        if (contour_response.IsFailure || contour_response.Data is null) {
            var errors = contour_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Generate Contour Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        //_contour_curve = new Vector3Collection(contour_response.Data.Select(v => new Vector3((float)v.X, (float)v.Y, (float)v.Z)));
        var parting_response = PartingTools.PartingMesh(_parting_curve.Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)), 20);
        if (parting_response.IsFailure || parting_response.Data is null) {
            var errors = parting_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Triangulate Split Mesh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _partingMesh = parting_response.Data;

        return;
        // generate parting mesh
        var response = MeshTools.GeneratePartingMesh(model, [], _draftPullDirection, 10.0);

        if (response.IsFailure || response.Data is null) {
            var errors = response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Generate Split Mesh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _partingMesh = response.Data;

        // joining the meshes
        response = MeshTools.JoinMeshes(_partingMesh, _draftAngleMeshPositive.ToMeshModel());

        if (response.IsFailure || response.Data is null) {
            var errors = response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Generate Split Mesh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _partingMesh = response.Data;

        // create inflated mesh to boolean insersect with the parting mesh
        MeshModel[] meshes = []; 
        MeshModel offset_mesh = new (MeshTools.OffsetMesh(model, _model_thickness)); // simulates a defines mold shape

        // tool mesh, combination of bolus and air channels
        
        var channels = WeakReferenceMessenger.Default.Send<AirChannelsRequestMessage>().Response;
        var tools = MeshModel.Combine(channels.Select(c => c.Value.Geometry.ToMeshModel()).ToArray());
        var task = Task.Run(() => meshes = MeshTools.FinalPass(model, offset_mesh, _partingMesh));
        task.Wait(); // needed or else mesh can randomly return no mesh

        if ( meshes.Length == 0 ) { return; } //mesh failed

        _partPositiveModel = meshes[0];
        _partNegativeModel = meshes[1];

        // remove air channels
        _partPositiveModel = MeshTools.BooleanSubtraction(_partPositiveModel, tools).Data;

        tools = MeshModel.Combine(channels.Select(c => c.Value.Geometry.ToMeshModel()).ToArray());
        _partNegativeModel = MeshTools.BooleanSubtraction(_partNegativeModel, tools).Data;

        return;

    }
}




