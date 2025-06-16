using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public class SmoothSceneManager : SceneManager {
    private BolusModel _bolus;
    private Material _surfaceDistanceSkin;

    public SmoothSceneManager() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateBolus(m.Bolus));

        SetDistancesTexture();
        UpdateBolus(null);
    }

    private void SetDistancesTexture() {
        //gradient color
        var faultColor = new Color4(1, 0, 0, 1); // red for faults
        var warningColor = new Color4(1, 1, 0, 1); // yellow for warnings
        var defaultColor = new Color4(0.5f, 0.5f, 0.5f, 1); // default color for within tolerance
        var passColor = new Color4(0, 1, 0, 1); // good color for within tolerance
        var excessColor = new Color4(0, 0, 1, 1); // blue for excess material

        List<Color4> colors = [
            ..GetGradients(faultColor, warningColor, 20), //bottom end, too far within
            ..GetGradients(warningColor, defaultColor, 20), //warning color transition
            ..GetGradients(defaultColor, passColor, 20), //fault color transition, upper angle setting
            ..GetGradients(passColor, excessColor, 20), //fault color section, ends at 90 degrees
        ];

        _surfaceDistanceSkin = new ColorStripeMaterial {
            ColorStripeX = colors,
            ColorStripeY = colors
        };


    }

    private static List<Color4> GetGradients(Color4 start, Color4 end, int steps) {
        float stepA = ((end.Alpha - start.Alpha) / (steps - 1));
        float stepR = ((end.Red - start.Red) / (steps - 1));
        float stepG = ((end.Green - start.Green) / (steps - 1));
        float stepB = ((end.Blue - start.Blue) / (steps - 1));

        List<Color4> colors = [];
        for (int i = 0; i < steps; i++) {
           colors.Add( new Color4(
                (start.Red + (stepR * i)),
                (start.Green + (stepG * i)),
                (start.Blue + (stepB * i)),
                (start.Alpha + (stepA * i))
            ));
        }

        return colors;
    }

    private static Vector2Collection GetTextureCoordinates(MeshModel model, MeshModel target) {
        float[] distances = MeshTools.SignedDistances(model, target).Select(d => (float)d).ToArray();

        float max = distances.Max();
        float min = distances.Min();

        MeshGeometry3D geometry = model.ToGeometry();
        Vector2Collection coordinates = new();
        for (int i = 0; i < geometry.Positions.Count; i++) {
            float value = (distances[i] - min) / (max - min); // normalize the distance value between 0 and 1
            value = Math.Clamp(value, 0, 1);
            coordinates.Add(new Vector2(value, value)); // Y coordinate for the color stripe

        }

        return coordinates;
    }

    private void UpdateBolus(BolusModel bolus) {
        _bolus = bolus;

        UpdateDisplay(bolus);
    }


    protected override void UpdateDisplay(BolusModel? bolus) {
        // get all of the current bolus
        var boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();
        if (boli.Length == 1) { // just the raw file
            var model = new DisplayModel3D {
                Geometry = boli[0].Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.SkyBlue,
            };

            models.Add(model);
        }

        //show smoothed mesh
        if (boli.Length == 2) { 
            var geometry = boli[1].Geometry;
            geometry.TextureCoordinates = GetTextureCoordinates(boli[1].Mesh, boli[0].Mesh);
            // smoothed mesh
            models.Add(new DisplayModel3D {
                Geometry = geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = _surfaceDistanceSkin,
                IsTransparent = false,
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));

    }


}
