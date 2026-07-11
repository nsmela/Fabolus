using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
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

    private Workspace Workspace { get; set; }
    private bool _isUpdatingFromScene;

    [ObservableProperty] private bool _isMould;
    [ObservableProperty] private IMesh? _activeMesh;

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
        _sceneManager = new PartingSplitSceneManager(engine);

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

    public Task ActivateAsync(Workspace workspace)
    {
        Workspace = workspace;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsSuccess)
        {
            ActiveMesh = activeMeshResult.Value;
            IsMould = ActiveMesh.Metadata.MouldDefinition().HasValue;

            _sceneManager.UpdateMesh(ActiveMesh);
            _isUpdatingFromScene = true;
            DirectionX = 0f; DirectionY = 1f; DirectionZ = 0f;
            _isUpdatingFromScene = false;
            _sceneManager.UpdateDirection(Vector3.UnitY);
        }

        PartingLine = null;
        HasPartingLine = false;
        InternalHoleCount = 0;

        return Task.CompletedTask;
    }

    public Task<Workspace> DeactivateAsync()
    {
        _sceneManager.ReleaseMesh();
        ActiveMesh = null;
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
            _sceneManager.ClearPartingPreview();
        }
    }

    [RelayCommand]
    public void GeneratePartingLine()
    {
        if (ActiveMesh is null) return;
        if (!IsMould)
        {
            _alert.ShowError("Parting split only applies to moulds.");
            return;
        }

        var direction = new Vector3(DirectionX, DirectionY, DirectionZ);
        var result = _partingLineFeature.Execute(ActiveMesh, direction);
        if (result.IsFailure)
        {
            _alert.ShowError(result.Error.Description);
            PartingLine = null;
            HasPartingLine = false;
            InternalHoleCount = 0;
            _sceneManager.ClearPartingPreview();
            return;
        }

        PartingLine = result.Value;
        InternalHoleCount = result.Value.InternalHoleCount;
        HasPartingLine = true;

        var boundsResult = _engine.Evaluators.GetStatistics(ActiveMesh);
        IMesh? tool = null;
        IMesh? positive = null;
        IMesh? negative = null;

        if (boundsResult.IsSuccess)
        {
            var toolResult = _engine.PartingTools.GenerateSplitTool(ActiveMesh, PartingLine, direction, boundsResult.Value);
            if (toolResult.IsSuccess)
            {
                tool = toolResult.Value;

                var positiveResult = _engine.Booleans.Intersect(ActiveMesh, tool);
                if (positiveResult.IsSuccess) positive = positiveResult.Value;

                var negativeResult = _engine.Booleans.Subtract(ActiveMesh, tool);
                if (negativeResult.IsSuccess) negative = negativeResult.Value;
            }
        }

        _sceneManager.ShowPartingPreview(PartingLine, direction, tool, positive, negative);
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

        _sceneManager.ReleaseMesh();
        _messenger.Send(new IsLoadingMessage(false));
        _messenger.Send(new SwitchToMeshManagerMessage());
    }

    [RelayCommand]
    public void ResetDirection()
    {
        _isUpdatingFromScene = true;
        DirectionX = 0f;
        DirectionY = 1f;
        DirectionZ = 0f;
        _isUpdatingFromScene = false;
        _sceneManager.UpdateDirection(Vector3.UnitY);

        HasPartingLine = false;
        PartingLine = null;
        InternalHoleCount = 0;
        _sceneManager.ClearPartingPreview();
    }
}
