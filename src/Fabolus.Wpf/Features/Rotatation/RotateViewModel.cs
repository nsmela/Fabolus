using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;
using GeometryMeshLib;
using System.Numerics;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Features.Rotatation;
public partial class RotateViewModel : ObservableObject, IViewState {
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;
    private readonly RotateSceneManager _sceneManager;

    private readonly TransformMesh _transformsFeature;

    private Workspace Workspace { get; set; }

    private bool _isLocked = false;

    [ObservableProperty] private float _xAxisAngle;
    [ObservableProperty] private float _yAxisAngle;
    [ObservableProperty] private float _zAxisAngle;

    partial void OnXAxisAngleChanged(float value) => SendTempRotation(Vector3.UnitX, value);
    partial void OnYAxisAngleChanged(float value) => SendTempRotation(Vector3.UnitY, value);
    partial void OnZAxisAngleChanged(float value) => SendTempRotation(Vector3.UnitZ, value);

    // Seeded from app preferences on every activation (see ActivateAsync). The values here are
    // only what a design-time instance shows, and are kept in step with the shipped defaults.
    [ObservableProperty] private float _warningAngle = 45.0f;
    [ObservableProperty] private float _criticalAngle = 65.0f;

    partial void OnWarningAngleChanged(float value) => SendOverhangSettings();
    partial void OnCriticalAngleChanged(float value) => SendOverhangSettings();

    private void ResetValues() {
        _isLocked = true;

        //setting slider values
        XAxisAngle = 0.0f;
        YAxisAngle = 0.0f;
        ZAxisAngle = 0.0f;
        _isLocked = false;

        _sceneManager.ApplyTempRotation(new Vector3D(0, 0 ,0), 0.0f);
    }

    public ISceneManager SceneManager => _sceneManager;


    public RotateViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine) {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;

        _sceneManager = new RotateSceneManager(_engine, _messenger);
        _transformsFeature = new TransformMesh(_engine);
    }

    public RotateViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), new GeometryEngine(new FileSystem())) { }

    public async Task ActivateAsync(Workspace workspace) {
        LoadOverhangPreferences();

        // Seed the gradient from the current slider values before the first render,
        // so the initial frame matches the warning/critical thresholds. The scene
        // manager skips rendering here because it has no mesh yet.
        _sceneManager.SetOverhangs(WarningAngle, CriticalAngle);

        await UpdateWorkspaceAsync(workspace);

        // clear mesh info
        _messenger.Send(new UpdateMeshInfoMessage([]));
    }

    public Task<Workspace> DeactivateAsync() {
        _sceneManager.ReleaseMesh();
        return Task.FromResult(Workspace);
    }

    private void SendTempRotation(Vector3 axis, float degrees) {
        if (_isLocked) return;

        // process the value
        _sceneManager.ApplyTempRotation(new Vector3D(axis.X, axis.Y, axis.Z), degrees);
    }

    private void SendOverhangSettings() =>
        _sceneManager.SetOverhangs(WarningAngle, CriticalAngle);

    /// <summary>
    /// Takes the overhang thresholds from app preferences, re-read each activation so a change
    /// made in the preferences window applies without restarting. Falls back to this view
    /// model's own defaults when the store cannot be reached, as in the design-time constructor.
    ///
    /// Critical is assigned first: the range slider will not let the lower thumb cross the
    /// upper one, so raising the ceiling before the floor keeps a preferred pair higher than
    /// the current one from being clamped. A stored pair that is inverted or too close together
    /// is dropped entirely, since neither half of it describes a usable gradient on its own.
    /// </summary>
    private void LoadOverhangPreferences() {
        float warning = AppPreferenceReader.Float(_messenger, UISettings.OverhangWarningAngleLabel,
            WarningAngle, PreferenceRanges.OverhangAngleMin, PreferenceRanges.OverhangAngleMax);
        float critical = AppPreferenceReader.Float(_messenger, UISettings.OverhangCriticalAngleLabel,
            CriticalAngle, PreferenceRanges.OverhangAngleMin, PreferenceRanges.OverhangAngleMax);

        if (warning + PreferenceRanges.OverhangMinGap > critical) { return; }

        CriticalAngle = critical;
        WarningAngle = warning;
    }

    // The scene manager only ever renders the active mesh, so that's all it gets -
    // the Workspace itself stays here in the view model.
    private async Task UpdateWorkspaceAsync(Workspace workspace) {
        Workspace = workspace;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) return;
        var activeMesh = activeMeshResult.Value;

        // GetMeshAtStage always returns an owned mesh (the view shows the model as it was
        // before any mould was cut); the scene manager takes ownership of it, since it
        // re-renders the mesh on every temp-rotation/overhang change.
        var stageResult = await Task.Run(() => CommandReplay.GetMeshAtStage(_engine, activeMesh, CommandPriority.Transform));
        if (stageResult.IsFailure) return;

        _sceneManager.UpdateMesh(stageResult.Value);
    }

    private void ShowAxisRotation(Vector3 axis) {
        _sceneManager.ShowAxisRotation(axis);
    }

    [RelayCommand] public void ShowAxisXRotation() => ShowAxisRotation(Vector3.UnitX);
    [RelayCommand] public void ShowAxisYRotation() => ShowAxisRotation(Vector3.UnitY);
    [RelayCommand] public void ShowAxisZRotation() => ShowAxisRotation(Vector3.UnitZ);
    [RelayCommand] public void HideAxisRotation() => ShowAxisRotation(Vector3.Zero);

    [RelayCommand]
    public async Task SaveAxisRotationAsync() {
        //which axis?
        Vector3 axis;
        float degrees;
        if (XAxisAngle != 0) {
            degrees = XAxisAngle;
            axis = Vector3.UnitX;
        } else if (YAxisAngle != 0) {
            degrees = YAxisAngle;
            axis = Vector3.UnitY;
        } else if (ZAxisAngle != 0) {
            degrees = ZAxisAngle;
            axis = Vector3.UnitZ;
        } else {
            return;
        }

        var activeId = Workspace.ActiveMeshId;
        var result = _transformsFeature.Rotate(
            Workspace,
            Workspace.ActiveMeshId,
            degrees * (float)(Math.PI / 180.0f),
            axis);

        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }

        _messenger.Send(new IsLoadingMessage(true));

        await UpdateWorkspaceAsync(result.Value);

        _messenger.Send(new IsLoadingMessage(false));

        ResetValues();
    }

    [RelayCommand]
    public async Task ClearRotationsAsync() {
        var activeId = Workspace.ActiveMeshId;
        var result = _transformsFeature.ClearRotation(Workspace, Workspace.ActiveMeshId);

        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }

        await UpdateWorkspaceAsync(result.Value);

        ResetValues();
    }
}