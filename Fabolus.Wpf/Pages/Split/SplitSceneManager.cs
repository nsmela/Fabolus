using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Core.Meshes.PartingTools;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Core.Extensions;
using System.Windows.Input;
using Fabolus.Core.BolusModel;

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
    private MeshGeometry3D _occludedRegion = new();

    // intersections
    private MeshGeometry3D _intersectionsOpenMesh = new();
    private MeshGeometry3D _intersectionsClosedMesh = new();

    // boundaries
    private MeshGeometry3D _boundariesMesh = new();

    // curves
    private MeshGeometry3D _curveNoneRegion = new();
    private MeshGeometry3D _curveFlatRegion = new();
    private MeshGeometry3D _curveValleyRegion = new();
    private MeshGeometry3D _curveRidgeRegion = new();

    // parting line tool
    private PartingTool _partingTool;
    private DisplayModel3D? _partingToolPreview = new();
    private DisplayModel3D _partingToolDisplay = new();
    private DisplayModel3D[] _partingToolAnchors = [];
    private Guid[] _validIds = [];

    // view options
    private SplitViewOptions _view_options;

    public SplitSceneManager() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;

        // PartingTool
        _partingTool = new(bolus.TransformedMesh(), []);
        _partingTool.Compute();
        _partingToolDisplay = new DisplayModel3D {
            Geometry = _partingTool.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Bisque
        };

        // request messages
        WeakReferenceMessenger.Default.Register<SplitSceneManager, UpdateSplitViewOptionsMessage>(this, (r, m) => {
            _view_options = m.Options;
            UpdateDisplay();
        });
        WeakReferenceMessenger.Default.Register<SplitSceneManager, SplitResultsMessage>(this, (r,m) => {
            UpdateResults(m.Results);
        });

        _settings.Model = bolus.TransformedMesh();
        _view_options = WeakReferenceMessenger.Default.Send<SplitRequestViewOptionsMessage>().Response;

        var results = WeakReferenceMessenger.Default.Send(new SplitRequestResultsMessage()).Response;
        UpdateResults(results);
    }

    protected override void OnMouseDown(List<HitTestResult> hits, InputEventArgs args) {
        if (hits is null || hits.Count() == 0) { return; }

        //catch and ignored mouse buttons and exit
        var mouse = args as MouseButtonEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
                || mouse.MiddleButton == MouseButtonState.Pressed) {
            return;
        }

        // check if the bolus is hit
        // show path
        var modelHit = hits.FirstOrDefault(x => _validIds.Contains(x.Geometry.GUID));
        if (modelHit is null) { return; }

        var point = modelHit.PointHit;
        var normal = modelHit.NormalAtHit;

        _partingTool.AddAnchor(point);
        _partingTool.Compute();
        _partingToolDisplay = new DisplayModel3D {
            Geometry = _partingTool.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Obsidian
        };

        // show anchors
        List<DisplayModel3D> meshes = [];
        var anchors = _partingTool.Model.GetVertices(_partingTool.AnchorIndexes.ToArray()).Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
        var normals = _partingTool.Model.GetVtxNormals(_partingTool.AnchorIndexes).Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();
        for(int i = 0; i < anchors.Length; i++) {
            MeshBuilder builder = new();
            builder.AddCylinder(anchors[i], anchors[i] + normals[i] * 4.0f, 1.0, 32, true, true);

            var model = new DisplayModel3D {
                Geometry = builder.ToMeshGeometry3D(),
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Obsidian
            };

            meshes.Add(model);
        }

        _partingToolAnchors = meshes.ToArray();

        UpdateDisplay();
    }

    protected override void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
        // reset parting tool
        _partingToolPreview = null;
        if (hits is null || hits.Count() == 0) {
            UpdateDisplay();
            return;
        }

        // convert mouse arguments
        var mouse = args as MouseEventArgs;
        if (mouse is null) {
            UpdateDisplay();
            return;
        }

        // process if any mouse button is down
        if (mouse.RightButton == MouseButtonState.Pressed
                || mouse.MiddleButton == MouseButtonState.Pressed
                || mouse.LeftButton == MouseButtonState.Pressed) {
            UpdateDisplay();
            return;
        }

        // check if the parting line setup is hit
        // TODO

        // check if the bolus is hit
        var modelHit = hits.FirstOrDefault(x => _validIds.Contains(x.Geometry.GUID));
        if (modelHit is null) {
            UpdateDisplay();
            return;
        }

        var point = modelHit.PointHit;
        //var normal = modelHit.NormalAtHit;

        _partingToolPreview = new DisplayModel3D {
            Geometry = _partingTool.PreviewAnchor(point),
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Bisque
        };

        UpdateDisplay();
    }

    private void UpdateResults(CuttingMeshResults results) {
        _bolus = results.Model.ToGeometry();

        // path for the parting line
        // testing
        Vector3[] path = PartingTools.GeneratePartingLine(results.Model, new System.Numerics.Vector3(0, 1, 0)).Select(v => ToVector3(v)).ToArray();
        MeshBuilder builder = new();
        foreach (Vector3 v in path.Select(v => new Vector3(v.X, v.Y, v.Z))) {
            builder.AddSphere(v, 0.25);
        }
        _partingPathMesh = builder.ToMeshGeometry3D();

        _partingMesh = results.CuttingMesh.ToGeometry();
        _mouldMesh = results.Mould is not null ? results.Mould.ToGeometry() : new();

        // draft regions
        _positiveRegion = results.DraftRegions[DraftRegions.DraftRegionClassification.Positive].ToGeometry();
        _negativeRegion = results.DraftRegions[DraftRegions.DraftRegionClassification.Negative].ToGeometry();
        _neutralRegion = results.DraftRegions[DraftRegions.DraftRegionClassification.Neutral].ToGeometry();

        // used for hit detection
        _validIds = [
            _positiveRegion.GUID,
            _negativeRegion.GUID,
            _neutralRegion.GUID,
        ];

        // final parted meshes
        _negativePullMesh = MeshModel.IsNullOrEmpty(results.NegativePullMesh)
            ? new()
            : results.NegativePullMesh.ToGeometry();
        _positivePullMesh = MeshModel.IsNullOrEmpty(results.PositivePullMesh) 
            ? new() 
            : results.PositivePullMesh.ToGeometry();

        // intersections
        MeshBuilder open = new();
        MeshBuilder closed = new();
        foreach (var intersections in results.Intersections) {
            int count = intersections.Points.Length;

            // initial parsing
            if (intersections.IsClosed) {
                closed.AddCylinder(ToVector3(intersections.Points.Last()), ToVector3(intersections.Points.First()), 0.1);
            } else {
                open.AddCylinder(ToVector3(intersections.Points.Last()), ToVector3(intersections.Points.First()), 0.1);
            }

            for (int i = 0; i < count; i++) {
                var v0 = ToVector3(intersections.Points[(i - 1 + count) % count]);
                var v1 = ToVector3(intersections.Points[i]);

                if (intersections.IsClosed) {
                    closed.AddCylinder(v0, v1, 0.1);
                } else {
                    open.AddCylinder(v0, v1, 0.1);
                }
            }

        }
        
        _intersectionsClosedMesh = closed.ToMeshGeometry3D();
        _intersectionsOpenMesh = open.ToMeshGeometry3D();

        // boundaries
        builder = new();
        Contour[] boundaries = MeshTools.GetHoles(results.CuttingMesh).Data;
        foreach(Contour contour in boundaries) {
            int count = contour.Points.Length;
            if (contour.IsClosed) {
                builder.AddCylinder(ToVector3(contour.Points.Last()), ToVector3(contour.Points.First()), 0.1);
            }
            
            for (int i = 0; i < count - 1; i++) {
                var v0 = ToVector3(contour.Points[i]);
                var v1 = ToVector3(contour.Points[i + 1]);

                builder.AddCylinder(v0, v1, 0.1);
            }

        }

        _boundariesMesh = builder.ToMeshGeometry3D();

        // curves
        var curves = MeshTools.GenerateCurveRegions(results.Model);
        _curveNoneRegion = curves[MeshTools.CurveRegionClassification.None].ToGeometry();
        _curveFlatRegion = curves[MeshTools.CurveRegionClassification.Flat].ToGeometry();
        _curveValleyRegion = curves[MeshTools.CurveRegionClassification.Valley].ToGeometry();
        _curveRidgeRegion = curves[MeshTools.CurveRegionClassification.Ridge].ToGeometry();

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

        // show curves
        if (_view_options.ShowCurves) {
            models.Add(new DisplayModel3D {
                Geometry = _curveNoneRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.White,
            });

            models.Add(new DisplayModel3D {
                Geometry = _curveFlatRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Green,
            });

            models.Add(new DisplayModel3D {
                Geometry = _curveValleyRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });

            models.Add(new DisplayModel3D {
                Geometry = _curveRidgeRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Red,
            });
        }

        // parting line
        if (_view_options.ShowPartingLine) {
            models.Add(new DisplayModel3D {
                Geometry = _partingPathMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Yellow,
            });

            if (DisplayModel3D.IsValid(_partingToolPreview)) {
                models.Add(_partingToolPreview.Value);
            }

            if (DisplayModel3D.IsValid(_partingToolDisplay)) {
                models.Add(_partingToolDisplay);
            }

            foreach (DisplayModel3D anchors in _partingToolAnchors) {
                models.Add(anchors);
            }
        }

        // parting mesh
        if (_partingMesh is not null && _view_options.ShowPartingMesh) {
            models.Add(new DisplayModel3D {
                Geometry = _partingMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });

            models.Add(new DisplayModel3D {
                Geometry = _intersectionsClosedMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Emerald,
            });

            models.Add(new DisplayModel3D {
                Geometry = _intersectionsOpenMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
            });

            models.Add(new DisplayModel3D {
                Geometry = _boundariesMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Brass,
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




