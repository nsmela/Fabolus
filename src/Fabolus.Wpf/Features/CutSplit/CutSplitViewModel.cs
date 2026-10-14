using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.CutSplit;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;
using System.Numerics;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.CutSplit;

public partial class CutSplitViewModel : ObservableObject, IViewState {
    
    private readonly IAlertDialog _alert;
    private readonly IDialogueSystem _dialogue;
    private readonly IMessenger _messenger;
    private readonly IGeometryEngine _engine;
    private readonly CutSplitSceneManager _sceneManager;
    private readonly CutMeshFeature _cutFeature;

    private Workspace Workspace { get; set; }

    [ObservableProperty] private bool _isMould;
    [ObservableProperty] private IMesh? _activeMesh;
    
    [ObservableProperty] private float _planeX;
    [ObservableProperty] private float _planeY;
    [ObservableProperty] private float _planeZ;
    [ObservableProperty] private float _planePitch;
    [ObservableProperty] private float _planeYaw;

    [ObservableProperty] private float _planeMinX = -100f;
    [ObservableProperty] private float _planeMaxX = 100f;
    [ObservableProperty] private float _planeMinY = -100f;
    [ObservableProperty] private float _planeMaxY = 100f;
    [ObservableProperty] private float _planeMinZ = -100f;
    [ObservableProperty] private float _planeMaxZ = 100f;

    private bool _isUpdatingFromScene = false;

    partial void OnPlaneXChanged(float value) => UpdatePlane();
    partial void OnPlaneYChanged(float value) => UpdatePlane();
    partial void OnPlaneZChanged(float value) => UpdatePlane();
    partial void OnPlanePitchChanged(float value) => UpdatePlane();
    partial void OnPlaneYawChanged(float value) => UpdatePlane();

    public CutSplitViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine, IDialogueSystem dialogue) {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;
        _dialogue = dialogue;

        _cutFeature = new CutMeshFeature(engine);
        _sceneManager = new CutSplitSceneManager(engine, messenger);
        
        _sceneManager.PlaneChanged += (origin, normal) => {
            _isUpdatingFromScene = true;
            PlaneX = origin.X;
            PlaneY = origin.Y;
            PlaneZ = origin.Z;
            
            float reconstructedPitch = -(float)System.Math.Asin(normal.Y);
            float reconstructedYaw = (float)System.Math.Atan2(normal.X, normal.Z);

            PlanePitch = reconstructedPitch * (float)(180.0 / System.Math.PI);
            PlaneYaw = reconstructedYaw * (float)(180.0 / System.Math.PI);
            _isUpdatingFromScene = false;
        };
    }

    public CutSplitViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), GeometryEngine.BspGeometryEngine.Create(), new DialogueSystem(WeakReferenceMessenger.Default)) { }

    public ISceneManager SceneManager => _sceneManager;

    public Task ActivateAsync(Workspace workspace) {
        Workspace = workspace;
        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsSuccess) {
            ActiveMesh = activeMeshResult.Value;
            
            var statsResult = _engine.Evaluators.GetStatistics(ActiveMesh);
            if (statsResult.IsSuccess) {
                var stats = statsResult.Value;
                var sizeX = (float)(stats.BoundsMax.X - stats.BoundsMin.X);
                var sizeY = (float)(stats.BoundsMax.Y - stats.BoundsMin.Y);
                var sizeZ = (float)(stats.BoundsMax.Z - stats.BoundsMin.Z);

                // Give 50% extra bounds on either side
                PlaneMinX = (float)stats.BoundsMin.X - sizeX * 0.5f;
                PlaneMaxX = (float)stats.BoundsMax.X + sizeX * 0.5f;
                PlaneMinY = (float)stats.BoundsMin.Y - sizeY * 0.5f;
                PlaneMaxY = (float)stats.BoundsMax.Y + sizeY * 0.5f;
                PlaneMinZ = (float)stats.BoundsMin.Z - sizeZ * 0.5f;
                PlaneMaxZ = (float)stats.BoundsMax.Z + sizeZ * 0.5f;

                _isUpdatingFromScene = true;
                PlaneX = (float)((stats.BoundsMin.X + stats.BoundsMax.X) / 2.0);
                PlaneY = (float)((stats.BoundsMin.Y + stats.BoundsMax.Y) / 2.0);
                PlaneZ = (float)((stats.BoundsMin.Z + stats.BoundsMax.Z) / 2.0);
                PlanePitch = 0f;
                PlaneYaw = 0f;
                _isUpdatingFromScene = false;
            } else {
                PlaneMinX = -100f;
                PlaneMaxX = 100f;
                PlaneMinY = -100f;
                PlaneMaxY = 100f;
                PlaneMinZ = -100f;
                PlaneMaxZ = 100f;

                _isUpdatingFromScene = true;
                PlaneX = 0f;
                PlaneY = 0f;
                PlaneZ = 0f;
                PlanePitch = 0f;
                PlaneYaw = 0f;
                _isUpdatingFromScene = false;
            }

            // A generated mould is one carrying a MouldDefinition, which is what the Mould feature
            // records. Matching on the mesh's name used to stand in for this, and could not work:
            // the name on the geometry is whatever the boolean that built the mould called it.
            var recordResult = Workspace.GetActiveRecord();
            IsMould = recordResult.IsSuccess && recordResult.Value.MouldDefinition() is not null;


            _sceneManager.UpdateMesh(ActiveMesh);
            UpdatePlane();
        }
        return Task.CompletedTask;
    }

    public Task<Workspace> DeactivateAsync() {
        _sceneManager.ReleaseMesh();
        ActiveMesh = null;
        return Task.FromResult(Workspace);
    }

    private void UpdatePlane() {
        if (_isUpdatingFromScene) return;

        var origin = new Vector3(PlaneX, PlaneY, PlaneZ);
        var rotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(PlaneYaw * (float)(System.Math.PI / 180.0), PlanePitch * (float)(System.Math.PI / 180.0), 0);
        var snNormal = global::System.Numerics.Vector3.Transform(global::System.Numerics.Vector3.UnitZ, rotation);
        var normal = new Vector3(snNormal.X, snNormal.Y, snNormal.Z);
        
        _sceneManager.UpdatePlane(new System.Numerics.Vector3((float)origin.X, (float)origin.Y, (float)origin.Z), new System.Numerics.Vector3((float)normal.X, (float)normal.Y, (float)normal.Z));
    }

    [RelayCommand]
    public async Task ApplyCutAsync() {
        if (ActiveMesh is null) return;
        if (IsMould) {
            _alert.ShowError("Split operation is not yet implemented for moulds.");
            return;
        }

        _messenger.Send(new IsLoadingMessage(true));

        var origin = new Vector3(PlaneX, PlaneY, PlaneZ);
        var rotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(PlaneYaw * (float)(System.Math.PI / 180.0), PlanePitch * (float)(System.Math.PI / 180.0), 0);
        var snNormal = global::System.Numerics.Vector3.Transform(global::System.Numerics.Vector3.UnitZ, rotation);
        var normal = new Vector3(snNormal.X, snNormal.Y, snNormal.Z);

        var recordResult = Workspace.GetActiveRecord();
        if (recordResult.IsFailure) {
            _alert.ShowError(recordResult.Error.Description);
            _messenger.Send(new IsLoadingMessage(false));
            return;
        }

        var record = recordResult.Value;
        var result = await Task.Run(() => _cutFeature.Execute(ActiveMesh, record, origin, normal));
        if (result.IsFailure) {
            _alert.ShowError(result.Error.Description);
            _messenger.Send(new IsLoadingMessage(false));
            return;
        }

        var (top, bottom) = result.Value;

        // Add to workspace - each half is its own entry, with the identity the cut gave it.
        var topWsResult = Workspace.AddMesh(top.Mesh, top.Record, setActive: false);
        if (topWsResult.IsSuccess) Workspace = topWsResult.Value;

        var botWsResult = Workspace.AddMesh(bottom.Mesh, bottom.Record, setActive: false);
        if (botWsResult.IsSuccess) Workspace = botWsResult.Value;

        // Clear active mesh selection so none is selected
        var activeClearResult = Workspace.SetActiveMesh(Guid.Empty);
        if (activeClearResult.IsSuccess) Workspace = activeClearResult.Value;

        _sceneManager.ReleaseMesh();
        _messenger.Send(new IsLoadingMessage(false));
        _messenger.Send(new SwitchToMeshManagerMessage());
    }

    [RelayCommand]
    public void ResetPlane() {
        if (ActiveMesh is null) return;
        var statsResult = _engine.Evaluators.GetStatistics(ActiveMesh);
        if (statsResult.IsSuccess) {
            var stats = statsResult.Value;
            _isUpdatingFromScene = true;
            var centre = (stats.BoundsMin + stats.BoundsMax) / 2.0;
            PlaneX = (float)centre.X;
            PlaneY = (float)centre.Y;
            PlaneZ = (float)centre.Z;
            PlanePitch = 0f;
            PlaneYaw = 0f;
            _isUpdatingFromScene = false;
            
            UpdatePlane();
        }
    }
}
