using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Core.Meshes.PolygonTools;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using static Fabolus.Core.Meshes.PolygonTools.PolygonTools;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public class SmoothSceneManager : SceneManagerBase {
    private Material _surfaceDistanceSkin;
    private ComparitivePolygon? contour;
    private ViewModes _view = ViewModes.None;

    protected override void RegisterMessages() {
        _messenger.Register<BolusUpdatedMessage>(this, (r, m) => UpdateDisplay());
        _messenger.Register<SmoothingContourMessage>(this, (r, m) => UpdateContour(m.Height));
        _messenger.Register<SmoothingViewModeMessage>(this, (r, m) => {
            _view = m.ViewMode;
            UpdateDisplay();
        });
    }

    public SmoothSceneManager() {
        RegisterMessages();
        RegisterInputBindings();
        SetDistancesTexture();
        UpdateDisplay();

    }

    private void SetDistancesTexture() {
        //gradient color
        var faultColor = new Color4(1, 0, 0, 1); // red for faults
        var warningColor = new Color4(1, 1, 0, 1); // yellow for warnings
        var defaultColor = new Color4(0.5f, 0.5f, 0.5f, 1); // default color for within tolerance
        var passColor = new Color4(0, 1, 0, 1); // good color for within tolerance
        var excessColor = new Color4(0, 0, 1, 1); // blue for excess material

        List<Color4> colors = [
            ..GetGradients(faultColor, warningColor, 30), //bottom end, too far within
            ..GetGradients(warningColor, defaultColor, 30), //warning color transition
            ..GetGradients(defaultColor, passColor, 30), //fault color transition
            ..GetGradients(passColor, excessColor, 30), //fault color section
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
        float[] distances = MeshTools.SignedDistances(target, model).Select(d => (float)d).ToArray();

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

    private void UpdateContour(double height) {
        contour = null;
        var boli = _messenger.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length < 2) { return; }

        var result = PolygonTools.ComparativeMeshSlice(boli[0].TransformedMesh(), boli[1].TransformedMesh(), height);
        if (result is null || result.UnionMesh.IsEmpty()) { return; }
        contour = result;

        UpdateDisplay();
    }

    private void UpdateDisplay() {
        // get all of the current bolus
        var boli = _messenger.Send(new AllBolusRequestMessage()).Response;
        if (BolusModel.IsNullOrEmpty(boli)) {
            _messenger.Send(new MeshDisplayUpdatedMessage([]));
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
            _messenger.Send(new MeshDisplayUpdatedMessage(models));
            return;
        }

        var geometry = boli[1].Geometry;
        switch (_view) {
            case ViewModes.None:
                models.Add(new DisplayModel3D {
                    Geometry = geometry,
                    Transform = MeshHelper.TransformEmpty,
                    Skin = PhongMaterials.Green,
                    IsTransparent = false,
                });
                break;

            case ViewModes.DistanceHeatMap:
                geometry.TextureCoordinates = GetTextureCoordinates(boli[1].Mesh, boli[0].Mesh);
                models.Add(new DisplayModel3D {
                    Geometry = geometry,
                    Transform = MeshHelper.TransformEmpty,
                    Skin = _surfaceDistanceSkin,
                    IsTransparent = false,
                });
                break;

            case ViewModes.Contouring:
                // show transparent smoothed model
                models.Add(new DisplayModel3D {
                    Geometry = geometry,
                    Transform = MeshHelper.TransformEmpty,
                    Skin = PhongMaterials.Emerald,
                    IsTransparent = true,
                });

                // show contours
                if (contour is not null) {
                    models.Add(new DisplayModel3D {
                        Geometry = contour.UnionMesh.ToGeometry(),
                        Transform = MeshHelper.TransformEmpty,
                        Skin = PhongMaterials.Green,
                    });

                    models.Add(new DisplayModel3D {
                        Geometry = contour.BodyMesh.ToGeometry(),
                        Transform = MeshHelper.TransformEmpty,
                        Skin = PhongMaterials.Red,
                    });

                    models.Add(new DisplayModel3D {
                        Geometry = contour.ToolMesh.ToGeometry(),
                        Transform = MeshHelper.TransformEmpty,
                        Skin = PhongMaterials.Blue,
                    });
                }

                break;

            default:
                break;
        }

        _messenger.Send(new MeshDisplayUpdatedMessage(models));
    }
}
