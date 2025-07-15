using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Wpf.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using static Fabolus.Core.Meshes.MeshTools.MeshTools;
using static Fabolus.Wpf.Bolus.BolusStore;

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
    private MeshModel _partingMesh;

    public SplitSceneManager() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        BolusId = bolus?.Geometry?.GUID;

        // request messages
        WeakReferenceMessenger.Default.Register<SplitSceneManager, SplitRequestModels>(this, (r,m) => m.Reply([r._partNegativeModel, r._partPositiveModel]));

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

        models.Add(new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = _skin
        });

        //foreach (var channel in _channels.Values) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = channel.Geometry,
        //        Transform = MeshHelper.TransformEmpty,
        //        Skin = channel.GUID == _activeChannel.GUID
        //         ? _selectedSkin
        //         : _channelSkin
        //    });
        //}

        // display region where the parting line can travel
        //if (_partingRegion is not null) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = _partingRegion,
        //        Transform = MeshHelper.TransformEmpty,
        //        Skin = _partingMaterial,
        //    });
        //}

        // show draft angle results
        //if (_draftAngleMeshPositive is not null) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = _draftAngleMeshPositive,
        //        Transform = MeshHelper.TransformEmpty,
        //        Skin = DiffuseMaterials.Green,
        //    });
        //}
        //
        //if (_draftAngleMeshNegative is not null) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = _draftAngleMeshNegative,
        //        Transform = MeshHelper.TransformEmpty,
        //        Skin = DiffuseMaterials.Red,
        //    });
        //}
        //
        //if (_draftAngleMeshNeutral is not null) {
        //    models.Add(new DisplayModel3D {
        //        Geometry = _draftAngleMeshNeutral,
        //        Transform = MeshHelper.TransformEmpty,
        //        Skin = DiffuseMaterials.Gray,
        //    });
        //}

        // parting curve
        //if (_parting_curve.Count > 0) {
        //    MeshBuilder builder = new();
        //    builder.AddTube(_parting_curve, 0.3, 16, true);
        //    models.Add(new DisplayModel3D {
        //        Geometry = builder.ToMeshGeometry3D(),
        //        Transform = MeshHelper.TransformEmpty,
        //        Skin = DiffuseMaterials.Yellow,
        //    });
        //}

        if (_previewMesh is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _previewMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = _previewSkin,
            });
        }

        if (_partNegativeModel is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _partNegativeModel.ToGeometry(),
                Transform = MeshHelper.TranslationFromAxis(0, -15, 0),
                Skin = DiffuseMaterials.Red,
            });
        }

        if (_partPositiveModel is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _partPositiveModel.ToGeometry(),
                Transform = MeshHelper.TranslationFromAxis(0, 15, 0),
                Skin = PhongMaterials.Blue,
            });
        }

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

    private static DraftClassification ReClassifyNeutral(MeshModel model, int tId, Dictionary<int, DraftClassification> results) {
        var neighbours = model.GetTriangleNeighbours(tId);

        int n0 = neighbours[0];
        int n1 = neighbours[1];
        int n2 = neighbours[2];

        List<int> ids = [];
        if (n0 >= 0 && results[n0] != DraftClassification.NEUTRAL) { ids.Add(n0); }
        if (n1 >= 0 && results[n1] != DraftClassification.NEUTRAL) { ids.Add(n1); }
        if (n2 >= 0 && results[n2] != DraftClassification.NEUTRAL) { ids.Add(n2); }

        if (ids.Count == 0) { return DraftClassification.NEUTRAL; } // no neighbours to classify
        if (ids.Count == 1) { return results[ids[0]]; } // only one neighbour, use its classification

        if (results[ids[0]] == results[ids[1]]) { return results[ids[0]]; }
        if (ids.Count == 2) { return DraftClassification.NEGATIVE; } // two neighbours, but they are not the same

        if (results[ids[0]] == results[ids[2]] || results[ids[1]] == results[ids[2]]) { // three neighbours, two are the same
            return results[n2];
        }

        return DraftClassification.NEGATIVE;
    }

    private void SetDraftMeshes(MeshModel model) {
        // draft angle meshes

        MeshBuilder positive_mesh = new();
        MeshBuilder negative_mesh = new();
        MeshBuilder neutral_mesh = new();

        Vector3 v0, v1, v2; // to be used in the loop
        double[] values;

        Dictionary<int, DraftClassification> results = MeshTools.DraftAngleAnalysis(model, _draftPullDirection, DRAFT_ANGLE_THRESHOLD_DEGREES);
        Queue<int> remaining = [];

        // first pass tp check if triangle is not connected to anything and add it to neutral if so
        foreach (var (tId, result) in results) {
            var neighbours = model.GetTriangleNeighbours(tId);
            if (neighbours[0] < 0 || neighbours[1] < 0 || neighbours[2] < 0) { // no neighbours
                results[tId] = DraftClassification.NEUTRAL; // mark as neutral
                remaining.Enqueue(tId);
                continue;
            }

            Console.WriteLine(tId);
            if (result != DraftClassification.NEUTRAL &&
                results[neighbours[0]] != result &&
                results[neighbours[1]] != result &&
                results[neighbours[2]] != result) {

                results[tId] = DraftClassification.NEUTRAL; // mark as neutral if all neighbours are not the same classification
            }

            if ((results[tId] == DraftClassification.NEUTRAL)) { remaining.Enqueue(tId); }

        }

        // second pass to eliminate neutrals
        int id = -1;
        while (remaining.Count > 0) {
            id = remaining.Dequeue();
            results[id] = ReClassifyNeutral(model, id, results); // change classification based on neighbours
        }

        // third and final pass
        foreach (var (key, value) in results) {
            if (value != DraftClassification.NEUTRAL) { continue; }

            results[key] = ReClassifyNeutral(model, key, results); // reclassify neutral triangles based on neighbours
        }

        // add triangles to meshes
        foreach (var (tId, result) in results) {
            values = model.GetTriangleAsDoubles(tId);
            v0 = new Vector3((float)values[0], (float)values[1], (float)values[2]);
            v1 = new Vector3((float)values[3], (float)values[4], (float)values[5]);
            v2 = new Vector3((float)values[6], (float)values[7], (float)values[8]);

            if (result == MeshTools.DraftClassification.POSITIVE) { positive_mesh.AddTriangle(v0, v1, v2); }
            if (result == MeshTools.DraftClassification.NEGATIVE) { negative_mesh.AddTriangle(v0, v1, v2); }
            if (result == MeshTools.DraftClassification.NEUTRAL) { neutral_mesh.AddTriangle(v0, v1, v2); }
        }

        _draftAngleMeshPositive = positive_mesh.ToMeshGeometry3D();
        _draftAngleMeshNegative = negative_mesh.ToMeshGeometry3D();
        _draftAngleMeshNeutral = neutral_mesh.ToMeshGeometry3D();

        // parting line
        // find edges for parting line and smooth that path
        var region_tris_ids = results.Where(x => x.Value == DraftClassification.NEGATIVE).Select(x => x.Key).ToArray();
        var path_vert_ids  = model.GetBorderEdgeLoop(region_tris_ids).ToArray();
        path_vert_ids = MeshTools.RemoveSingleTriangles(model, path_vert_ids);
        var path = model.GetVertices(path_vert_ids).Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2]));
        _parting_curve = new Vector3Collection(path.ToArray());

        // generate parting mesh
        _partingMesh = MeshTools.GeneratePartingMesh(model, path_vert_ids, _draftPullDirection, 10.0);
        _partingMesh = MeshTools.JoinMeshes(_partingMesh, _draftAngleMeshPositive.ToMeshModel());
        MeshModel[] meshes = []; 
        MeshModel offset_mesh = new (MeshTools.OffsetMesh(model, _model_thickness)); // simulates a defines mold shape
        var task = Task.Run(() => meshes = MeshTools.FinalPass(model, offset_mesh, _partingMesh));
        task.Wait(); // needed or else mesh can randomly return no mesh

        if ( meshes.Length == 0 ) { return; } //mesh failed

        _partPositiveModel = meshes[0];
        _partNegativeModel = meshes[1];
        return;

        // final parting meshes
        _partPositiveModel = _partingMesh;

        MeshModel model_offset = new MeshModel(MeshTools.OffsetMesh(model, 0.2f));
        MeshModel b_offset = new MeshModel(MeshTools.OffsetMesh(model, 3.0f));
        var mesh_result = MeshTools.BooleanSubtraction(b_offset, model_offset);
        if (mesh_result.IsFailure || mesh_result.Data == null) { return; }

        mesh_result = MeshTools.BooleanSubtraction(mesh_result.Data, _partingMesh);
        if (mesh_result.IsFailure || mesh_result.Data == null) { return; }

        //b_offset = new MeshModel(MeshTools.OffsetMesh(model, 0.15f));
        //mesh_result = MeshTools.BooleanSubtraction(mesh_result.Data, b_offset);
        //if (mesh_result.IsFailure || mesh_result.Data == null) { return; }

        _partNegativeModel = mesh_result.Data;
        return;
        //_partingMesh = MeshTools.JoinMeshes(_partingMesh, _draftAngleMeshNegative.ToMeshModel());
        //task = Task.Run(() => _partingMesh = MeshTools.FinalPass(model, _partingMesh));
        //task.Wait();
        //mesh_result = MeshTools.BooleanSubtraction(_partingMesh, model);
        //if (mesh_result.IsFailure || mesh_result.Data == null) { return; }
        //_partNegativeModel = mesh_result.Data;
    }
}




