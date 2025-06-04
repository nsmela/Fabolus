using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Fabolus.Wpf.Bolus.BolusStore;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Fabolus.Wpf.Pages.Smooth;

public class SmoothSceneManager : SceneManager {
    public const float DEFAULT_SURFACE_DISTANCE = 2.0f;

    private BolusModel _bolus;
    private MeshModel[] _greenModels = [];
    private MeshModel[] _redModels = [];
    private Material _surfaceDistanceSkin;
    private Material _smoothSkin = PhongMaterials.Blue;
    private float _surfaceDistance = DEFAULT_SURFACE_DISTANCE;
    private float _contour_height = 0.0f;

    private Dictionary<float, MeshGeometry3D> _rawContours = [];
    private Dictionary<float, MeshGeometry3D> _smoothContours = [];

    public SmoothSceneManager() {
        _surfaceDistanceSkin = SkinHelper.SurfaceDifferenceSkin(_surfaceDistance);

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateBolus(m.Bolus));
        WeakReferenceMessenger.Default.Register<SmoothingContourMessage>(this, (r, m) => UpdateContouringHeight(m.z_height));
    }

    private void UpdateBolus(BolusModel bolus) {
        _bolus = bolus;

        GenerateContours();

        UpdateDisplay(bolus);
    }

    private void GenerateContours() {
        BolusModel[] boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length < 2) {
            _smoothContours.Clear();
            return; // not enough data to generate contours
        }

        var raw = boli[0].TransformedMesh().ToGeometry();
        var smoothed = boli[1].TransformedMesh().ToGeometry();
        var min = smoothed.Bound.Minimum.Z;
        var max = smoothed.Bound.Maximum.Z;

        _rawContours.Clear();
        _smoothContours.Clear();

        float layer = (int)min;
        while (layer + min < max) {
            // contour line around smoothed
            Vector3 plane = new(0, 0, layer);
            Vector3 normal = Vector3.UnitZ;
            List<Vector3> contour = MeshGeometryHelper.GetContourSegments(smoothed, plane, normal).ToList() ?? new();

            List<int> edges = new();
            for (int i = 0; i < contour.Count; i++) {
                edges.Add(i);
            }
            edges.Add(0); // close the loop

            MeshBuilder builder = new();
            builder.AddPipes(contour, edges, 0.4f);
            foreach (var point in contour) {
                builder.AddSphere(point, 0.2f);
            }

            _smoothContours.Add(layer, builder.ToMeshGeometry3D());

            // contour mesh for raw
            _rawContours.Add(layer, MeshTools.Contour(raw.ToMeshModel(), layer).ToGeometry());

            layer++;
        }
    }

    private void UpdateContouringHeight(float value) {
        _contour_height = value;
        UpdateDisplay(_bolus);
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
            // smoothed mesh
            models.Add(new DisplayModel3D {
                Geometry = boli[1].Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Emerald,
                IsTransparent = true,
            });

            if (_smoothContours.Count == 0) { GenerateContours(); } // generate contours if not already done

            // contour around smoothed mesh
            if (_smoothContours.ContainsKey(_contour_height)) {
                models.Add(new DisplayModel3D {
                    Geometry = _smoothContours[_contour_height],
                    Transform = MeshHelper.TransformEmpty,
                    Skin = DiffuseMaterials.Yellow,
                });
            }

            // flat contour mesh for raw mesh            
            var raw_contour_mesh = SmoothingTools.Contour(boli[0].TransformedMesh(), _contour_height);
            models.Add(new DisplayModel3D {
                Geometry = _rawContours[_contour_height],
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Blue,
            });

        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));

    }


}
