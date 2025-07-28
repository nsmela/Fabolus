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
    private MeshGeometry3D _offsetMesh;
    private MeshGeometry3D _innerMesh;
    private MeshGeometry3D _segmentsMesh;

    // view options
    private SplitViewOptions _view_options;

    public SplitSceneManager() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        BolusId = bolus?.Geometry?.GUID;

        // request messages
        WeakReferenceMessenger.Default.Register<SplitSceneManager, SplitRequestModelsMessage>(this, (r,m) => m.Reply([r._partNegativeModel, r._partPositiveModel]));
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

        // show draft angle results
        if (_draftAngleMeshPositive is not null && _view_options.ShowPullRegions) {
            models.Add(new DisplayModel3D {
                Geometry = _draftAngleMeshPositive,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Green,
            });
        }
        
        if (_draftAngleMeshNegative is not null && _view_options.ShowPullRegions) {
            models.Add(new DisplayModel3D {
                Geometry = _draftAngleMeshNegative,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Red,
            });
        }
        
        if (_draftAngleMeshNeutral is not null && _view_options.ShowPullRegions) {
            models.Add(new DisplayModel3D {
                Geometry = _draftAngleMeshNeutral,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Gray,
            });
        }

        // parting curve
        if (_parting_curve.Count > 0 && _view_options.ShowPartingLine) {
            MeshBuilder builder = new();
            builder.AddTube(_parting_curve, 0.3, 16, true);
            models.Add(new DisplayModel3D {
                Geometry = builder.ToMeshGeometry3D(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });

            builder = new();
            builder.AddSphere(_parting_curve.First(), 0.5);
            builder.AddSphere(_parting_curve.Last(), 0.5);
            models.Add(new DisplayModel3D {
                Geometry = builder.ToMeshGeometry3D(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
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

            // shows defective segments
            models.Add(new DisplayModel3D {
                Geometry = _segmentsMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Violet,
            });
        }

        if(_partingMesh is not null && _view_options.ShowPartingMesh) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh.ToGeometry(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });
        }

        double spacing = _view_options.ExplodePartingMeshes ? 15 : 0;
        if (_partNegativeModel is not null && _view_options.ShowNegativeParting) {
            models.Add(new DisplayModel3D {
                Geometry = _partNegativeModel.ToGeometry(),
                Transform = MeshHelper.TranslationFromAxis(0, -spacing, 0),
                Skin = DiffuseMaterials.Red,
            });
        }
        
        if (_partPositiveModel is not null && _view_options.ShowPositiveParting) {
            models.Add(new DisplayModel3D {
                Geometry = _partPositiveModel.ToGeometry(),
                Transform = MeshHelper.TranslationFromAxis(0, spacing, 0),
                Skin = DiffuseMaterials.Green,
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
        var curve_response = PartingTools.OrientedPartingLine(model);
        if (curve_response.IsFailure || curve_response.Data is null) {
            var errors = curve_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Generate Oriented Parting Line Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var curve = PartingTools.GeneratePartingLine(model);

        _parting_curve = new Vector3Collection(PartingTools.OrientedPartingLine(model, curve).Data.Select(v => new Vector3(v.X, v.Y, v.Z)));

        // calculate the offset polyline curve
        var offset_response = PartingTools.OffsetPath(model, curve, 15.0f);

        MeshBuilder builder = new();
        builder.AddTube(new Vector3Collection(offset_response.Select(v => new Vector3(v.X, v.Y, v.Z))), 0.3, 16, true);
        _offsetMesh = builder.ToMeshGeometry3D();

        // 
        // calculate the offset polyline curve
        var inner_response = PartingTools.OffsetPath(model, curve, -1.0f);

        builder = new();
        builder.AddTube(new Vector3Collection(inner_response.Select(v => new Vector3(v.X, v.Y, v.Z))), 0.3, 16, true);
        _innerMesh = builder.ToMeshGeometry3D();

        // show defective segments
        var segments = PartingTools.SegmentIntersections(offset_response);
        builder = new();
        foreach (System.Numerics.Vector3[] segment in segments) {
            
            builder.AddCylinder(ToVector3(segment[0]), ToVector3(segment[1]), 0.5, 16, true);
        }
        _segmentsMesh = builder.ToMeshGeometry3D();

        // creates the parting mesh to boolean subtract from the main mould
        var parting_response = PartingTools.JoinPolylines(inner_response.ToArray(), offset_response.ToArray());
        if (parting_response.IsFailure || parting_response.Data is null) {
            var errors = parting_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Mesh Stiching Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _partingMesh = parting_response.Data;

        return;
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

        // creates the parting mesh to boolean subtract from the main mould
        parting_response = PartingTools.EvenPartingMesh(_parting_curve.Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)), 20, extrude_distance: 0.15);
        if (parting_response.IsFailure || parting_response.Data is null) {
            var errors = parting_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Triangulate Split Mesh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _partingMesh = parting_response.Data;

        // create inflated mesh to boolean intersect with the parting mesh
        var offset_mesh = WeakReferenceMessenger.Default.Send<MouldRequestMessage>().Response;

        var boolean_response = MeshTools.BooleanSubtraction(offset_mesh, _partingMesh);
        if (boolean_response.IsFailure || boolean_response.Data is null) {
            var errors = boolean_response.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Offset mesh subtraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                _partPositiveModel = models[0];
                _partNegativeModel = null; // empty model for negative part
                break;
            case (2):
                var bounds0 = models[0].BoundsLower();
                var bounds1 = models[1].BoundsLower();

                if (bounds0[1] > bounds1[1]) {
                    _partPositiveModel = models[0]; // if the y coordinate of the first mesh is lower than the second, then it is the positive part
                    _partNegativeModel = models[1];
                } else {
                    _partPositiveModel = models[1]; // if the y coordinate of the first mesh is lower than the second, then it is the positive part
                    _partNegativeModel = models[0];
                }
                break;
            default:
                MessageBox.Show("More than two models found after boolean subtraction. Please check the model.", "Split Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
        }

    }

    private static Vector3 ToVector3(System.Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);
    
}




