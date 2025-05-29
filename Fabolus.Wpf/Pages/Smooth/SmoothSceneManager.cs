using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Meshes;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public class SmoothSceneManager : SceneManager {
    public const float DEFAULT_SURFACE_DISTANCE = 2.0f;

    private BolusModel _bolus;
    private MeshModel[] _greenModels = [];
    private MeshModel[] _redModels = [];
    private Material _surfaceDistanceSkin;
    private Material _smoothSkin = PhongMaterials.Blue;
    private float _surfaceDistance = DEFAULT_SURFACE_DISTANCE;

    public SmoothSceneManager() {
        _surfaceDistanceSkin = SkinHelper.SurfaceDifferenceSkin(_surfaceDistance);

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateBolus(m.Bolus));
        WeakReferenceMessenger.Default.Register<SmoothingModelsUpdatedMessage>(this, (r,m) => UpdateSmoothingModels(m.GreenModels, m.RedModels));

    }

    private void UpdateBolus(BolusModel bolus) {
        _bolus = bolus;
        UpdateDisplay(bolus);
    }

    private void UpdateSmoothingModels(MeshModel[] greens, MeshModel[] reds) {
        _greenModels = greens;
        _redModels = reds;

        UpdateDisplay(_bolus);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        // get all of the current bolus
        var boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        //set each model for display
        var models = new List<DisplayModel3D>();
        foreach (BolusModel b in boli) {
            var model = new DisplayModel3D {
                Geometry = b.Geometry,
                Transform = MeshHelper.TransformEmpty,
            };
            switch (b.BolusType) {

                case BolusType.Raw:
                    model = model with {
                        Skin = PhongMaterials.MediumGray,
                        IsTransparent = true,
                        ShowWireframe = true,
                    };
                    break;

                case BolusType.Smooth:
                    model = model with { 
                        Skin = DiffuseMaterials.Emerald,
                    };
                    break;
                default:
                    break;
            }

            models.Add(model);
        }

        //smooth surface testing
        var mesh = boli.Length > 1 ? boli[1] : boli[0];
        var surfaceMeshes = SmoothingTools.GetSmoothSurfaces(mesh.Mesh, Math.PI/8.0f);
        Array.Sort(surfaceMeshes, (a, b) => b.Mesh.TriangleCount.CompareTo(a.Mesh.TriangleCount));

        for (int i = 0; i <  surfaceMeshes.Length; i++) {

            var display = new DisplayModel3D {
                Geometry = surfaceMeshes[i].ToGeometry(),
                Transform = MeshHelper.TransformEmpty,
                Skin = i > 1 ? DiffuseMaterials.Red : DiffuseMaterials.Blue,
            };

            models.Add(display);
        };

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));

    }

}
