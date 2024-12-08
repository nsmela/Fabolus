using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
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

    private Material _surfaceDistanceSkin;
    private float _surfaceDistance = DEFAULT_SURFACE_DISTANCE;

    public SmoothSceneManager() {
        _surfaceDistanceSkin = SkinHelper.SurfaceDifferenceSkin(_surfaceDistance);

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateDisplay(m.Bolus));

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
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
        models.Add(new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = material
        });

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

}
