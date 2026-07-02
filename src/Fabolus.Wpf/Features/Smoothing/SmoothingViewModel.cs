using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features.Smoothing;

public partial class SmoothingViewModel : ObservableObject, IViewState {
    
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly SmoothingSceneManager _sceneManager;

    private readonly ResetSmoothing _resetFeature;
    private readonly SmoothMesh _smoothFeature;

    private Workspace Workspace { get; set; }
    private Guid ActiveMeshId { get; set; }

    [ObservableProperty] private int _iterations = 1;
    [ObservableProperty] private float _intensity = 1.5f;
    [ObservableProperty] private float _inflation = 0.2f;
    [ObservableProperty] private float _remeshRatio = 1.0f;
    [ObservableProperty] private float _resolution = 1.0f;
    [ObservableProperty] private double _heatmapSensitivity = 0.4;
    [ObservableProperty] private bool _hasActiveMesh;
    [ObservableProperty] private bool _isSmoothed;
    [ObservableProperty] private string _applyButtonText = "Apply Smoothing";
    [ObservableProperty] private bool _showHeatmap;
    [ObservableProperty] private bool _showGhost;
    [ObservableProperty] private bool _showComparisonSlider;
    [ObservableProperty] private double _comparisonFactor = 0.5;

    [ObservableProperty] private SmoothDisplayMode _displayMode = SmoothDisplayMode.None;

    partial void OnShowHeatmapChanged(bool value) => UpdateViewport();
    partial void OnHeatmapSensitivityChanged(double value) => UpdateViewport();
    partial void OnShowGhostChanged(bool value) => UpdateViewport();
    partial void OnShowComparisonSliderChanged(bool value) => UpdateViewport();
    partial void OnComparisonFactorChanged(double value) => UpdateViewport();

    public SmoothingViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine) {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;

        _resetFeature = new ResetSmoothing(_engine);
        _sceneManager = new SmoothingSceneManager(engine);
        _smoothFeature = new SmoothMesh(engine);
    }

    public SmoothingViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), new GeometryMeshLib.GeometryEngine(new FileSystem())) { }

    public ISceneManager SceneManager => _sceneManager;

    public void Activate(Workspace workspace) {
        UpdateWorkspace(workspace);

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsSuccess) {

            var settingsResult = activeMeshResult.Value.Metadata.GetSmoothing();

            var settings = settingsResult.HasValue
                ? settingsResult.Value
                : new SmoothSettings();

            UpdateSettings(settings);

        }
    }

    public Workspace Deactivate() => Workspace;

    partial void OnDisplayModeChanged(SmoothDisplayMode value) {
        _sceneManager.SetDisplayMode(value);
        UpdateViewport();
    }

    private void UpdateSettings(SmoothSettings settings) {
        Iterations = settings.Iterations;
        Inflation = settings.Inflation;
        Intensity = settings.Intensity;
        RemeshRatio = settings.RemeshRatio;
        Resolution = settings.Resolution;
    }

    private void UpdateViewport() {
        UpdateWorkspace(Workspace);
    }

    private void UpdateWorkspace(Workspace workspace) {
        Workspace = workspace;

        var meshResult = Workspace.GetActiveMesh();
        if (meshResult.IsFailure) return;
        var mesh = meshResult.Value;

        // Comparison views (heatmap, cross-section) compare against the mesh's aligned
        // "unsmoothed twin" - BaseMesh with the remaining commands (e.g. a rotation) replayed
        // on top - NOT raw BaseMesh, which stays pristine and never rotates, so it drifts out
        // of alignment as soon as the mesh is transformed after smoothing.
        IMesh? unsmoothedMesh = null;
        if (DisplayMode != SmoothDisplayMode.None && mesh.Metadata.GetSmoothing().HasValue) {
            var unsmoothedResult = _resetFeature.ComputeUnsmoothedMesh(mesh);
            if (unsmoothedResult.IsSuccess) {
                unsmoothedMesh = unsmoothedResult.Value;
            }
        }

        double[]? heatmapColors = null;
        if (DisplayMode == SmoothDisplayMode.Heatmap && unsmoothedMesh is not null) {
            var colorResult = _engine.Evaluators.CalculateDeviationColors(mesh, unsmoothedMesh, HeatmapSensitivity);
            if (colorResult.IsSuccess) {
                heatmapColors = colorResult.Value;
            }
        }

        PublishInfo(mesh);
        // The scene manager only ever renders the active mesh, so that's all it gets -
        // the Workspace itself stays here in the view model.
        _sceneManager.UpdateMesh(mesh, unsmoothedMesh, heatmapColors);

        // The twin is a scratch mesh and the scene manager has already converted it to render
        // geometry - but when no other commands existed, the replay hands back the stored
        // BaseMesh itself, which must not be disposed.
        if (unsmoothedMesh is not null && !ReferenceEquals(unsmoothedMesh, mesh.Metadata.BaseMesh.Value)) {
            unsmoothedMesh.Dispose();
        }
    }

    private void PublishInfo(IMesh activeMesh) {
        var items = new List<MeshInfoItem>();

        var baseMeshResult = activeMesh.Metadata.BaseMesh;
        if (baseMeshResult.HasValue) {
            var originalStats = _engine.Evaluators.GetStatistics(baseMeshResult.Value).Value;
            items.Add(new TitleInfoItem { Label = "Original Mesh" });
            items.Add(new TextInfoItem { Label = "Volume", Value = $"{originalStats.Volume:N2} mL" });
            items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{(originalStats.SurfaceArea/100):N2} mm²" });
            items.Add(new TextInfoItem { Label = "Triangles", Value = originalStats.TriangleCount.ToString("N0") });
        }

        var settingsResult = activeMesh.Metadata.GetSmoothing();
        if (settingsResult.HasValue) 
        {
            var stats = activeMesh.Metadata.MeshStats().Value;
            items.Add(new TitleInfoItem { Label = "Smoothed Mesh" });
            items.Add(new TextInfoItem { Label = "Volume", Value = $"{stats.Volume:N2} mL" });
            items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{(stats.SurfaceArea / 100):N2} mm²" });
            items.Add(new TextInfoItem { Label = "Triangles", Value = stats.TriangleCount.ToString("N0") });
        }

        _messenger.Send(new UpdateMeshInfoMessage(items));
    }

    [RelayCommand]
    public void ApplySmoothing() {
        var settings = new SmoothSettings(
            Iterations,
            Intensity,
            Inflation,
            RemeshRatio,
            Resolution);

        var result = _smoothFeature.Execute(Workspace, settings);
        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }
       
        UpdateWorkspace(result.Value);
    }

    [RelayCommand]
    public void ResetSmoothing() {
        var result = _resetFeature.Execute(Workspace);
        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }

        UpdateWorkspace(result.Value);
    }
}

public enum ViewModes {
    None,
    DistanceHeatMap,
    Contouring
}
