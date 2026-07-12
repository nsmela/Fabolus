using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;
using System.Numerics;
using System.Threading.Tasks;
using PartingLine = Fabolus.Core.Geometry.PartingLine;

namespace Fabolus.Wpf.Features.PartingSplit;

/// <summary>
/// Lets the user pick a pull direction, generate the parting line for the active mould along
/// it (surfacing any internal holes that need their own shut-off surface), and commit the
/// split into two new workspace meshes. Only meaningful for moulds - see <see cref="IsMould"/>.
/// </summary>
public partial class PartingSplitViewModel : ObservableObject, IViewState
{
    private readonly IAlertDialog _alert;
    private readonly IMessenger _messenger;
    private readonly IGeometryEngine _engine;
    private readonly PartingSplitSceneManager _sceneManager;
    private readonly PartingLineFeature _partingLineFeature;
    private readonly SplitMouldFeature _splitFeature;
    private readonly ComputePartingDirectionColors _colorsFeature;

    private Workspace Workspace { get; set; }
    private bool _isUpdatingFromScene;

    [ObservableProperty] private bool _isMould;
    [ObservableProperty] private IMesh? _activeMesh;
    [ObservableProperty] private IMesh? _baseTransformMesh;
    [ObservableProperty] private IMesh? _toolMesh;
    [ObservableProperty] private IMesh? _positiveRegionMesh;
    [ObservableProperty] private IMesh? _negativeRegionMesh;

    [ObservableProperty] private PartingSplitState _currentState = PartingSplitState.DirectionSelection;

    [ObservableProperty] private float _directionX;
    [ObservableProperty] private float _directionY = 1f;
    [ObservableProperty] private float _directionZ;

    [ObservableProperty] private PartingLine? _partingLine;
    [ObservableProperty] private bool _hasPartingLine;
    [ObservableProperty] private int _internalHoleCount;

    partial void OnDirectionXChanged(float value) => OnDirectionChanged();
    partial void OnDirectionYChanged(float value) => OnDirectionChanged();
    partial void OnDirectionZChanged(float value) => OnDirectionChanged();

    public PartingSplitViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;

        _partingLineFeature = new PartingLineFeature(engine);
        _splitFeature = new SplitMouldFeature(engine);
        _colorsFeature = new ComputePartingDirectionColors(engine);
        _sceneManager = new PartingSplitSceneManager(engine, _colorsFeature);

        _sceneManager.DirectionChanged += direction =>
        {
            _isUpdatingFromScene = true;
            DirectionX = direction.X;
            DirectionY = direction.Y;
            DirectionZ = direction.Z;
            _isUpdatingFromScene = false;
        };

        Workspace = Workspace.CreateEmpty();
    }

    public PartingSplitViewModel() : this(
        WeakReferenceMessenger.Default,
        new AlertDialog(),
        new GeometryMeshLib.GeometryEngine(new FileSystem()))
    { }

    public ISceneManager SceneManager => _sceneManager;

    public async Task ActivateAsync(Workspace workspace)
    {
        Workspace = workspace;
        CurrentState = PartingSplitState.DirectionSelection;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsSuccess)
        {
            ActiveMesh = activeMeshResult.Value;
            IsMould = ActiveMesh.Metadata.MouldDefinition().HasValue;

            var stageResult = await Task.Run(() => CommandReplay.GetMeshAtStage(_engine, ActiveMesh, CommandPriority.Transform));
            if (stageResult.IsSuccess)
            {
                BaseTransformMesh = stageResult.Value;
            }
            else
            {
                BaseTransformMesh = ActiveMesh; // Fallback
            }

            await Task.Yield();

            _sceneManager.UpdateMeshes(ActiveMesh, BaseTransformMesh);
            _isUpdatingFromScene = true;
            DirectionX = 0f; DirectionY = 1f; DirectionZ = 0f;
            _isUpdatingFromScene = false;
            
            _sceneManager.UpdateDirection(Vector3.UnitY);
            _sceneManager.UpdateState(CurrentState);
        }

        PartingLine = null;
        HasPartingLine = false;
        InternalHoleCount = 0;
    }

    public Task<Workspace> DeactivateAsync()
    {
        _sceneManager.ReleaseMeshes();
        ActiveMesh = null;
        BaseTransformMesh = null;
        ToolMesh = null;
        PositiveRegionMesh = null;
        NegativeRegionMesh = null;
        PartingLine = null;
        HasPartingLine = false;
        return Task.FromResult(Workspace);
    }

    private void OnDirectionChanged()
    {
        var direction = new Vector3(DirectionX, DirectionY, DirectionZ);
        if (direction == Vector3.Zero) return;

        if (!_isUpdatingFromScene)
        {
            _sceneManager.UpdateDirection(Vector3.Normalize(direction));
        }

        // A new direction invalidates any previously generated parting line/preview.
        if (HasPartingLine)
        {
            HasPartingLine = false;
            PartingLine = null;
            InternalHoleCount = 0;
            ToolMesh = null;
            PositiveRegionMesh = null;
            NegativeRegionMesh = null;
            _sceneManager.ClearPartingPreview();
        }
    }

    [RelayCommand]
    public async Task NextStateAsync()
    {
        if (CurrentState == PartingSplitState.DirectionSelection)
        {
            if (ActiveMesh is null || BaseTransformMesh is null) return;
            if (!IsMould)
            {
                _alert.ShowError("Parting split only applies to moulds.");
                return;
            }

            _messenger.Send(new IsLoadingMessage(true));
            var direction = new Vector3(DirectionX, DirectionY, DirectionZ);

            var lineResult = await Task.Run(() => _partingLineFeature.Execute(BaseTransformMesh, direction));
            if (lineResult.IsFailure)
            {
                _alert.ShowError(lineResult.Error.Description);
                _messenger.Send(new IsLoadingMessage(false));
                return;
            }

            PartingLine = lineResult.Value;
            InternalHoleCount = PartingLine.InternalHoleCount;
            HasPartingLine = true;

            var generationResult = await Task.Run(() =>
            {
                IMesh? tool = null;
                IMesh? pos = null;
                IMesh? neg = null;
                string? error = null;
                var boundsResult = _engine.Evaluators.GetStatistics(ActiveMesh);
                if (boundsResult.IsSuccess)
                {
                    var toolResult = _engine.PartingTools.GenerateSplitTool(ActiveMesh, PartingLine, direction, boundsResult.Value);
                    if (toolResult.IsSuccess)
                    {
                        tool = toolResult.Value;
                        var positiveResult = _engine.Booleans.Intersect(ActiveMesh, tool);
                        if (positiveResult.IsSuccess) pos = positiveResult.Value;

                        var negativeResult = _engine.Booleans.Subtract(ActiveMesh, tool);
                        if (negativeResult.IsSuccess) neg = negativeResult.Value;
                    }
                    else
                    {
                        error = toolResult.Error.Description;
                    }
                }
                return (tool, pos, neg, error);
            });

            if (generationResult.tool == null)
            {
                _alert.ShowError($"Failed to generate split tool: {generationResult.error ?? "Unknown error"}");
                _messenger.Send(new IsLoadingMessage(false));
                return;
            }

            ToolMesh = generationResult.tool;
            PositiveRegionMesh = generationResult.pos;
            NegativeRegionMesh = generationResult.neg;

            _sceneManager.SetPreviewData(PartingLine, ToolMesh, PositiveRegionMesh, NegativeRegionMesh);
            _messenger.Send(new IsLoadingMessage(false));
        }

        if (CurrentState < PartingSplitState.FinalPartedMould)
        {
            CurrentState++;
            _sceneManager.UpdateState(CurrentState);
        }
    }

    [RelayCommand]
    public void PreviousState()
    {
        if (CurrentState > PartingSplitState.DirectionSelection)
        {
            CurrentState--;
            _sceneManager.UpdateState(CurrentState);
        }
    }

    [RelayCommand]
    public async Task ApplySplitAsync()
    {
        if (ActiveMesh is null || PartingLine is null) return;
        if (!IsMould)
        {
            _alert.ShowError("Parting split only applies to moulds.");
            return;
        }

        _messenger.Send(new IsLoadingMessage(true));

        var direction = new Vector3(DirectionX, DirectionY, DirectionZ);
        var meshId = ActiveMesh.Metadata.Id;
        var partingLine = PartingLine;

        var result = await Task.Run(() => _splitFeature.Execute(Workspace, meshId, partingLine, direction));

        if (result.IsFailure)
        {
            _alert.ShowError(result.Error.Description);
            _messenger.Send(new IsLoadingMessage(false));
            return;
        }

        Workspace = result.Value;

        _sceneManager.ReleaseMeshes();
        _messenger.Send(new IsLoadingMessage(false));
        _messenger.Send(new SwitchToMeshManagerMessage());
    }

    [RelayCommand]
    public void ResetDirection()
    {
        CurrentState = PartingSplitState.DirectionSelection;
        _isUpdatingFromScene = true;
        DirectionX = 0f;
        DirectionY = 1f;
        DirectionZ = 0f;
        _isUpdatingFromScene = false;
        _sceneManager.UpdateDirection(Vector3.UnitY);
        _sceneManager.UpdateState(CurrentState);

        HasPartingLine = false;
        PartingLine = null;
        InternalHoleCount = 0;
        ToolMesh = null;
        PositiveRegionMesh = null;
        NegativeRegionMesh = null;
        _sceneManager.ClearPartingPreview();
    }
}

