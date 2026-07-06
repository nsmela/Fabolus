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

    // Owned meshes cached between workspace changes, so display-only changes (heatmap
    // toggle, sensitivity/comparison sliders) re-render from them instead of re-fetching
    // and re-replaying per slider tick. Disposed on refresh and on Deactivate.
    private IMesh? _stagedMesh;
    private IMesh? _unsmoothedTwin;
    private MeshStatistics? _originalStats;

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

        var metadataResult = Workspace.GetActiveMeshMetadata();
        if (metadataResult.IsSuccess) {

            var settingsResult = metadataResult.Value.GetSmoothing();

            var settings = settingsResult.HasValue
                ? settingsResult.Value
                : new SmoothSettings();

            UpdateSettings(settings);

        }
    }

    public Workspace Deactivate() {
        ReleaseCachedMeshes();
        return Workspace;
    }

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
        RenderViewport();
    }

    private void UpdateWorkspace(Workspace workspace) {
        Workspace = workspace;
        RefreshMeshes();
        RenderViewport();
    }

    // Re-derives the cached meshes from the current workspace: the staged mesh shown in the
    // viewport (the model as it was before any mould was cut) and the original-mesh stats
    // for the info panel. The comparison twin is computed lazily in RenderViewport, since
    // it's only needed when a comparison display mode is active.
    private void RefreshMeshes() {
        ReleaseCachedMeshes();

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) return;
        var activeMesh = activeMeshResult.Value;

        var stageResult = CommandReplay.GetMeshAtStage(_engine, activeMesh, CommandPriority.Transform);
        if (stageResult.IsFailure) return;
        _stagedMesh = stageResult.Value;

        // The base mesh's stats were cached on its metadata at import time and it never
        // changes afterward - no geometry copy needed to read them.
        var baseMetadata = activeMesh.Metadata.BaseMeshMetadata;
        if (baseMetadata.HasValue) {
            var statsResult = baseMetadata.Value.MeshStats();
            if (statsResult.HasValue) _originalStats = statsResult.Value;
        }
    }

    private void RenderViewport() {
        if (_stagedMesh is null) return;

        // Comparison views (heatmap, cross-section) compare against the mesh's aligned
        // "unsmoothed twin" - BaseMesh with the remaining commands (e.g. a rotation) replayed
        // on top - NOT raw BaseMesh, which stays pristine and never rotates, so it drifts out
        // of alignment as soon as the mesh is transformed after smoothing.
        IMesh? unsmoothedMesh = null;
        if (DisplayMode != SmoothDisplayMode.None && _stagedMesh.Metadata.GetSmoothing().HasValue) {
            if (_unsmoothedTwin is null) {
                var unsmoothedResult = _resetFeature.ComputeUnsmoothedMesh(_stagedMesh);
                if (unsmoothedResult.IsSuccess) {
                    _unsmoothedTwin = unsmoothedResult.Value;
                }
            }
            unsmoothedMesh = _unsmoothedTwin;
        }

        double[]? heatmapColors = null;
        if (DisplayMode == SmoothDisplayMode.Heatmap && unsmoothedMesh is not null) {
            var colorResult = _engine.Evaluators.CalculateDeviationColors(_stagedMesh, unsmoothedMesh, HeatmapSensitivity);
            if (colorResult.IsSuccess) {
                heatmapColors = colorResult.Value;
            }
        }

        PublishInfo();
        // The scene manager only borrows the meshes for this call (it converts them to
        // render geometry immediately); ownership stays here with the cache.
        _sceneManager.UpdateMesh(_stagedMesh, unsmoothedMesh, heatmapColors);
    }

    private void ReleaseCachedMeshes() {
        _stagedMesh = null;
        _unsmoothedTwin = null;
        _originalStats = null;
    }

    private void PublishInfo() {
        var items = new List<MeshInfoItem>();

        if (_originalStats is not null) {
            items.Add(new TitleInfoItem { Label = "Original Mesh" });
            items.Add(new TextInfoItem { Label = "Volume", Value = $"{_originalStats.Volume:N2} mL" });
            items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{(_originalStats.SurfaceArea/100):N2} mm²" });
            items.Add(new TextInfoItem { Label = "Triangles", Value = _originalStats.TriangleCount.ToString("N0") });
        }

        var metadataResult = Workspace.GetActiveMeshMetadata();
        if (metadataResult.IsSuccess && metadataResult.Value.GetSmoothing().HasValue)
        {
            var statsResult = metadataResult.Value.MeshStats();
            if (statsResult.HasValue) {
                var stats = statsResult.Value;
                items.Add(new TitleInfoItem { Label = "Smoothed Mesh" });
                items.Add(new TextInfoItem { Label = "Volume", Value = $"{stats.Volume:N2} mL" });
                items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{(stats.SurfaceArea / 100):N2} mm²" });
                items.Add(new TextInfoItem { Label = "Triangles", Value = stats.TriangleCount.ToString("N0") });
            }
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
