using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Split;

public class SplitSceneManager : SceneManager {
    private MeshGeometry3D? _previewMesh;
    private Material _previewSkin = DiffuseMaterials.Turquoise;

    private Guid? BolusId;

    private Guid? PartingRegionId;
    private MeshGeometry3D _partingRegion;
    private Material _partingMaterial = DiffuseMaterials.Green;

    public SplitSceneManager() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        BolusId = bolus?.Geometry?.GUID;

        var parting_region = MeshTools.PartingRegion(bolus.Mesh);
        _partingRegion = parting_region.ToGeometry();
        PartingRegionId = _partingRegion.GUID;
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

        //models.Add(new DisplayModel3D {
        //    Geometry = bolus.Geometry,
        //    Transform = MeshHelper.TransformEmpty,
        //    Skin = _skin
        //});

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
        if (_partingRegion is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _partingRegion,
                Transform = MeshHelper.TransformEmpty,
                Skin = _partingMaterial,
            });
        }

        if (_previewMesh is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _previewMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = _previewSkin,
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

}
