using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
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

        _sceneManager = new RotateSceneManager(_engine);
        _transformsFeature = new TransformMesh(_engine);
    }

    public RotateViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), new GeometryEngine(new FileSystem())) { }

    public void Activate(Workspace workspace) {
        // Seed the gradient from the current slider values before the first render,
        // so the initial frame matches the warning/critical thresholds. The scene
        // manager skips rendering here because it has no mesh yet.
        _sceneManager.SetOverhangs(WarningAngle, CriticalAngle);

        UpdateWorkspace(workspace);

        // clear mesh info
        _messenger.Send(new UpdateMeshInfoMessage([]));
    }

    public Workspace Deactivate() => Workspace;

    private void SendTempRotation(Vector3 axis, float degrees) {
        if (_isLocked) return;

        // process the value
        _sceneManager.ApplyTempRotation(new Vector3D(axis.X, axis.Y, axis.Z), degrees);
    }

    private void SendOverhangSettings() =>
        _sceneManager.SetOverhangs(WarningAngle, CriticalAngle);

    // The scene manager only ever renders the active mesh, so that's all it gets -
    // the Workspace itself stays here in the view model.
    private void UpdateWorkspace(Workspace workspace) {
        Workspace = workspace;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) return;
        var activeMesh = activeMeshResult.Value;

        var stageResult = CommandReplay.GetMeshAtStage(_engine, activeMesh, CommandPriority.Transform);
        if (stageResult.IsFailure) return;
        var mesh = stageResult.Value;

        _sceneManager.UpdateMesh(mesh);

        if (!ReferenceEquals(mesh, activeMesh)) {
            mesh.Dispose();
        }
    }

    private void ShowAxisRotation(Vector3 axis) {
        _sceneManager.ShowAxisRotation(axis);
    }

    [RelayCommand] public void ShowAxisXRotation() => ShowAxisRotation(Vector3.UnitX);
    [RelayCommand] public void ShowAxisYRotation() => ShowAxisRotation(Vector3.UnitY);
    [RelayCommand] public void ShowAxisZRotation() => ShowAxisRotation(Vector3.UnitZ);
    [RelayCommand] public void HideAxisRotation() => ShowAxisRotation(Vector3.Zero);

    [RelayCommand]
    public void SaveAxisRotation() {
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

        UpdateWorkspace(result.Value);

        ResetValues();
    }

    [RelayCommand]
    public void ClearRotations() {
        var activeId = Workspace.ActiveMeshId;
        var result = _transformsFeature.ClearRotation(Workspace, Workspace.ActiveMeshId);

        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            return;
        }

        UpdateWorkspace(result.Value);

        ResetValues();
    }
}