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
using System.Numerics;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Features.Rotatation;
public partial class RotateViewModel : ObservableObject, IViewState {
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;
    private readonly BusyIndicator _busy;
    private readonly RotateSceneManager _sceneManager;

    private readonly TransformMesh _transformsFeature;

    private Workspace Workspace { get; set; }

    private bool _isLocked = false;

    /// <summary>
    /// True while a rotation is being applied or cleared.
    /// </summary>
    /// <remarks>
    /// The sliders and their hover ring are suppressed for the duration. The rotation is applied
    /// off the UI thread, so the panel stays live while it runs - and a temp rotation or an axis
    /// ring shown now would be drawn against a mesh that is in the middle of being replaced, on
    /// top of a rotation the user has already committed.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRotationEnabled))]
    private bool _isRotating;

    /// <summary>The inverse of <see cref="IsRotating"/>, for the sliders to bind IsEnabled to.</summary>
    public bool IsRotationEnabled => !IsRotating;

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

    // ---- Slider bounds -------------------------------------------------
    // Bound by the view rather than hardcoded in it, so the range a value can be given here is
    // the same one its preference is validated against. The two used to be separate literals
    // that had drifted apart, which let a user pick a default the tool would not accept - or
    // set a value in the tool that no default could express.

    public double OverhangAngleMinimum => RotationPreferences.Ranges.OverhangAngleMin;
    public double OverhangAngleMaximum => RotationPreferences.Ranges.OverhangAngleMax;

    /// <summary>How close the two thumbs may come. The pair is rejected below this.</summary>
    public double OverhangMinimumGap => RotationPreferences.Ranges.OverhangMinGap;

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
        _busy = new BusyIndicator(messenger);
        _alert = alert;
        _engine = engine;

        _sceneManager = new RotateSceneManager(_engine, _messenger);
        _transformsFeature = new TransformMesh(_engine);
    }

    public RotateViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), GeometryEngine.BspGeometryEngine.Create()) { }

    public async Task ActivateAsync(Workspace workspace) {
        LoadOverhangPreferences();

        // Seed the gradient from the current slider values before the first render,
        // so the initial frame matches the warning/critical thresholds. The scene
        // manager skips rendering here because it has no mesh yet.
        _sceneManager.SetOverhangs(WarningAngle, CriticalAngle);

        // The info panel is already empty: MainViewModel clears it as each view is swapped in,
        // and this view publishes nothing to it.
        await UpdateWorkspaceAsync(workspace);
    }

    public Task<Workspace> DeactivateAsync() {
        _sceneManager.ReleaseMesh();
        return Task.FromResult(Workspace);
    }

    private void SendTempRotation(Vector3 axis, float degrees) {
        if (_isLocked || IsRotating) return;

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
        var prefs = _messenger.GetSection(RotationPreferences.Default).Clamped();
        CriticalAngle = prefs.OverhangCriticalAngle;
        WarningAngle = prefs.OverhangWarningAngle;
    }

    // The scene manager only ever renders the active mesh, so that's all it gets -
    // the Workspace itself stays here in the view model.
    private async Task UpdateWorkspaceAsync(Workspace workspace) {
        Workspace = workspace;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) return;
        var activeMesh = activeMeshResult.Value;

        var recordResult = Workspace.GetActiveRecord();
        if (recordResult.IsFailure) return;
        var record = recordResult.Value;

        // GetMeshAtStage always returns an owned mesh (the view shows the model as it was
        // before any mould was cut); the scene manager takes ownership of it, since it
        // re-renders the mesh on every temp-rotation/overhang change.
        var stageResult = await Task.Run(() => CommandReplay.GetMeshAtStage(_engine, activeMesh, record, CommandPriority.Transform));
        if (stageResult.IsFailure) return;

        _sceneManager.UpdateMesh(stageResult.Value);
    }

    private void ShowAxisRotation(Vector3 axis) {
        // Hovering a slider while the rotation is being applied must not raise the ring. The
        // sliders are disabled for the duration, but the hover commands are reachable on their
        // own, so the rule lives here rather than only in the view.
        if (IsRotating) return;

        _sceneManager.ShowAxisRotation(axis);
    }

    /// <summary>
    /// Takes the axis ring down and holds off the sliders for as long as the scope is held.
    /// </summary>
    /// <remarks>
    /// A drag finishes with the pointer still over the slider, so the ring is up at the moment
    /// the rotation is committed. Left alone it would hang over the mesh for the whole rebuild.
    /// </remarks>
    private RotatingScope Rotating() => new(this);

    private readonly struct RotatingScope : IDisposable {
        private readonly RotateViewModel _owner;

        public RotatingScope(RotateViewModel owner) {
            _owner = owner;
            owner._sceneManager.ShowAxisRotation(Vector3.Zero);
            owner.IsRotating = true;
        }

        public void Dispose() => _owner.IsRotating = false;
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

        // The overlay covers the rotation itself, not just the re-render after it: transforming
        // every vertex of a large bolus is the quarter-second here, and it used to run on the UI
        // thread before the flag went up.
        using var busy = _busy.Enter();
        using var rotating = Rotating();

        var workspace = Workspace;
        var radians = degrees * (float)(Math.PI / 180.0f);
        var result = await Task.Run(() => _transformsFeature.Rotate(
            workspace,
            workspace.ActiveMeshId,
            radians,
            axis));

        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }

        await UpdateWorkspaceAsync(result.Value);

        ResetValues();
    }

    [RelayCommand]
    public async Task ClearRotationsAsync() {
        // Reverting the rotation replays the command list, which costs the same as applying it -
        // and this path had no overlay at all.
        using var busy = _busy.Enter();
        using var rotating = Rotating();

        var workspace = Workspace;
        var result = await Task.Run(() => _transformsFeature.ClearRotation(workspace, workspace.ActiveMeshId));

        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }

        await UpdateWorkspaceAsync(result.Value);

        ResetValues();
    }
}
