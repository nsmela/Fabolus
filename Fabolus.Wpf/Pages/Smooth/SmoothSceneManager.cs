using CommunityToolkit.Mvvm.Messaging;
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
    private float _surfaceDistance = DEFAULT_SURFACE_DISTANCE;

    public SmoothSceneManager() {
        _surfaceDistanceSkin = SkinHelper.SurfaceDifferenceSkin(_surfaceDistance);

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateBolus(m.Bolus));
        WeakReferenceMessenger.Default.Register<SmoothingModelsUpdatedMessage>(this, (r,m) => UpdateSmoothingModels(m.GreenModels, m.RedModels));
        
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
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

        Material material; 

        if (bolus.BolusType is BolusType.Smooth) {
            var boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
            var rawBolus = boli.Where(x => x.BolusType == BolusType.Raw).First();
            var coordinates = SmoothingTools.GenerateTextureCoordinates(bolus, rawBolus);
            
            var textcoords = new Vector2Collection();
            var count = bolus.Geometry.Positions.Count();
            for (int i = 0; i < count; i++) {
                textcoords.Add(new SharpDX.Vector2(0, coordinates[i]));
            }
            bolus.Geometry.TextureCoordinates = textcoords;

            material = _surfaceDistanceSkin;

        } else {
            material = PhongMaterials.Gray;
        }

        var models = new List<DisplayModel3D>();
        if (_greenModels.Count() > 0) {
            foreach(var model in _greenModels) {
                models.Add(new DisplayModel3D {
                    Geometry = model.ToGeometry(),
                    Transform = MeshHelper.TransformEmpty,
                    Skin = PhongMaterials.Emerald,
                });
            }
        }

        if (_redModels.Count() > 0) {
            foreach (var model in _redModels) {
                models.Add(new DisplayModel3D {
                    Geometry = model.ToGeometry(),
                    Transform = MeshHelper.TransformEmpty,
                    Skin = PhongMaterials.Ruby,
                });
            }
        }

        if (models.Count() == 0) {
            models.Add(new DisplayModel3D {
                Geometry = bolus.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = material
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

}
