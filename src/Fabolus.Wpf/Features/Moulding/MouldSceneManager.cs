using System.Numerics;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Core.Common;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Moulding;

public class MouldSceneManager : ISceneManager
{
    private readonly IGeometryEngine _engine;
    private Element3D _grid;

    private readonly Material _targetSkin = DiffuseMaterials.Gray;

    // The mould is only ever shown as a live preview before generation.
    private readonly Material _mouldSkin = DiffuseMaterials.Ruby;

    private readonly Material _channelSkin = DiffuseMaterials.Emerald;
    private readonly Material _selectedChannelSkin = DiffuseMaterials.Pearl;
    private readonly Material _previewChannelSkin = DiffuseMaterials.Pearl;

    private IMesh? TargetMesh { get; set; }
    private IReadOnlyList<AirChannelModel> Channels { get; set; } = [];
    private IAirChannel PreviewChannel { get; set; }

    private Guid _targetMeshId = Guid.Empty;
    private Guid _previewChannelId = Guid.Empty;
    private Guid _selectedChannelId = Guid.Empty;

    private MeshGeometryModel3D _mouldModel;
    private bool _mouldHiddenForHover;
    private bool _mouseOverTarget;

    // Painted-channel stroke capture: non-null while the left button is held and the user
    // is dragging a stroke across the target mesh.
    private List<Vector3>? _strokePoints;
    private bool StrokeActive => _strokePoints is not null;

    // Input decimation only - keeps a long drag to a manageable point count. The real
    // smoothing/resampling happens once, on commit, in the view model.
    private const float MinStrokePointDistance = 0.75f;

    // Which channel type the user has selected in the panel; left-down on the target mesh
    // starts a paint stroke for Painted and places a channel immediately for the others.
    public AirChannelType ActiveChannelType { get; set; } = AirChannelType.Straight;

    // While a mould has been generated, the target mesh IS the final result - channel
    // placement/selection/preview are all disabled until the user clears it.
    public bool IsMouldGenerated { get; set; }

    private readonly Dictionary<Guid, Guid> _channelVisualToModelId = [];
    private readonly Dictionary<Guid, Guid> _channelModelToVisualId = [];

    public event Action<Element3D> VisualAddedOrUpdated;
    public event Action<Guid> VisualRemovedById;
    public event Action VisualsCleared;

    public event Action<Vector3, Vector3> ChannelPlaced;
    public event Action<Guid> ChannelSelected;
    public event Action<Vector3, Vector3> ChannelHovered;
    public event Action DeleteSelectedChannelRequested;

    public event Action<IReadOnlyList<Vector3>> StrokeUpdated;
    public event Action<IReadOnlyList<Vector3>> StrokeCompleted;

    private readonly IMessenger _messenger;

    public MouldSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
        _engine = engine;
        _messenger = messenger;

        var width = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
        var depth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
        var show = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
        _grid = SceneHelpers.GenerateGrid(width, depth, 10, show);

        _messenger.Register<AppPreferenceUpdateMessage>(this, (r, m) => {
            if (m.Key == UISettings.PrintBedWidthLabel || m.Key == UISettings.PrintBedDepthLabel || m.Key == UISettings.ShowBedGridLabel) {
                var w = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
                var d = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
                var s = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
                
                if (_grid != null) {
                    VisualRemovedById?.Invoke(_grid.GUID);
                }
                _grid = SceneHelpers.GenerateGrid(w, d, 10, s);
                VisualAddedOrUpdated?.Invoke(_grid);
            }
        });
    }

    /// <summary>
    /// Takes ownership of <paramref name="mesh"/>: it's retained for mould previews and
    /// channel generation, and disposed when replaced or on <see cref="ReleaseMesh"/>.
    /// </summary>
    public Result UpdateMesh(IMesh mesh)
    {
        if (_targetMeshId != Guid.Empty)
            VisualRemovedById?.Invoke(_targetMeshId);

        TargetMesh = mesh;

        var geometryResult = TargetMesh.ToHelixMesh(_engine);
        if (geometryResult.IsFailure)
            return geometryResult.Error;

        var model = new MeshGeometryModel3D
        {
            Geometry = geometryResult.Value,
            // Once generated, the target mesh IS the mould result - keep it in the same
            // skin the preview used, rather than reverting to the plain target colour.
            Material = IsMouldGenerated ? _mouldSkin : _targetSkin,
            IsTransparent = IsMouldGenerated,
            CullMode = SharpDX.Direct3D11.CullMode.None,
        };

        _targetMeshId = model.GUID;
        VisualAddedOrUpdated?.Invoke(model);

        return Result.Success();
    }

    public Result UpdateMould(MouldDefinition mouldDefinition)
    {
        if (_mouldModel is not null)
        {
            VisualRemovedById?.Invoke(_mouldModel.GUID);
            _mouldModel = null;
        }

        if (TargetMesh is null)
            return Result.Success();

        var generateResult = mouldDefinition.Generate(_engine, TargetMesh);
        if (generateResult.IsFailure)
            return Result.Success(); // Invalid parameters mid-drag; just skip the preview silently.

        var mouldMesh = generateResult.Value;

        var geometryResult = mouldMesh.ToHelixMesh(_engine);
        if (geometryResult.IsFailure)
            return Result.Success();

        _mouldModel = new MeshGeometryModel3D
        {
            Geometry = geometryResult.Value,
            Material = _mouldSkin,
            // The mould shell encloses the target mesh; picking must fall through to the
            // mesh underneath so hovering/clicking on it can place air channels.
            IsHitTestVisible = false,
            Visibility = _mouldHiddenForHover ? Visibility.Hidden : Visibility.Visible,
        };

        VisualAddedOrUpdated?.Invoke(_mouldModel);

        return Result.Success();
    }

    /// <summary>
    /// Disposes the retained target mesh. Called when the owning view deactivates - the
    /// scene manager dies with its view model, so nothing else will release it.
    /// </summary>
    public void ReleaseMesh()
    {
        TargetMesh = null;
    }

    // Called once the mould has actually been generated: the mould shell and channel
    // markers are now baked into the new active mesh's geometry, so the pre-generation
    // overlays would just be stale duplicates on top of it.
    public void ClearPreviews()
    {
        if (_mouldModel is not null)
        {
            VisualRemovedById?.Invoke(_mouldModel.GUID);
            _mouldModel = null;
        }
        _mouldHiddenForHover = false;

        foreach (var visualId in _channelVisualToModelId.Keys.ToList())
            VisualRemovedById?.Invoke(visualId);
        _channelVisualToModelId.Clear();
        _channelModelToVisualId.Clear();
        Channels = [];
        _selectedChannelId = Guid.Empty;

        if (_previewChannelId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_previewChannelId);
            _previewChannelId = Guid.Empty;
        }
        PreviewChannel = null;

        // A stroke whose release was never observed (left the viewport) must not survive
        // mould generation - it would commit as a surprise channel on a later mouse move.
        _strokePoints = null;
    }

    private void SetMouldHiddenForHover(bool hidden)
    {
        if (_mouldHiddenForHover == hidden)
            return;

        _mouldHiddenForHover = hidden;

        if (_mouldModel is null)
            return;

        _mouldModel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        VisualAddedOrUpdated?.Invoke(_mouldModel);
    }

    public Result UpdateChannels(IReadOnlyList<AirChannelModel> channels)
    {
        foreach (var visualId in _channelVisualToModelId.Keys.ToList())
            VisualRemovedById?.Invoke(visualId);

        _channelVisualToModelId.Clear();
        _channelModelToVisualId.Clear();
        Channels = channels;

        foreach (var channel in Channels)
        {
            var generateResult = channel.DomainModel.Generate(_engine, AirChannelRenderMode.Full, TargetMesh);
            if (generateResult.IsFailure)
                continue;

            var channelMesh = generateResult.Value;
            var geometryResult = channelMesh.ToHelixMesh(_engine);
            if (geometryResult.IsFailure)
                continue;

            var model = new MeshGeometryModel3D
            {
                Geometry = geometryResult.Value,
                Material = channel.Id == _selectedChannelId ? _selectedChannelSkin : _channelSkin,
                CullMode = SharpDX.Direct3D11.CullMode.Back,
            };

            _channelVisualToModelId[model.GUID] = channel.Id;
            _channelModelToVisualId[channel.Id] = model.GUID;
            VisualAddedOrUpdated?.Invoke(model);
        }

        return Result.Success();
    }

    public void SelectChannel(Guid channelId)
    {
        _selectedChannelId = channelId;
        UpdateChannels(Channels);
        ChannelSelected?.Invoke(channelId);
    }

    public void UpdatePreviewChannel(IAirChannel channel)
    {
        PreviewChannel = channel;
        RenderPreviewChannel();
    }

    private void RenderPreviewChannel()
    {
        if (_previewChannelId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_previewChannelId);
            _previewChannelId = Guid.Empty;
        }

        if (IsMouldGenerated || PreviewChannel is null || !(_mouseOverTarget || StrokeActive))
            return;

        // The full extruded solid is too expensive to rebuild on every mouse event: show a
        // cheap swept tube along the stroke while dragging, and a plain sphere marker for a
        // single-point painted hover (Full would run the whole extrude+raycast pipeline,
        // including a fresh spatial index over the target mesh, per mouse move).
        var renderMode = StrokeActive ? AirChannelRenderMode.Cone
            : PreviewChannel is PaintedAirChannel { Path.Count: 1 } ? AirChannelRenderMode.Point
            : AirChannelRenderMode.Full;

        var generateResult = PreviewChannel.Generate(_engine, renderMode, TargetMesh);
        if (generateResult.IsFailure)
            return;

        var previewMesh = generateResult.Value;
        var geometryResult = previewMesh.ToHelixMesh(_engine);
        if (geometryResult.IsFailure)
            return;

        var model = new MeshGeometryModel3D
        {
            Geometry = geometryResult.Value,
            Material = _previewChannelSkin,
            IsHitTestVisible = false,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
        };

        _previewChannelId = model.GUID;
        VisualAddedOrUpdated?.Invoke(model);
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid);
    }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key)
    {
        if (StrokeActive && key == Key.Escape)
        {
            CancelStroke();
            return true;
        }

        if (!IsMouldGenerated && key == Key.Delete && _selectedChannelId != Guid.Empty)
        {
            DeleteSelectedChannelRequested?.Invoke();
            return true;
        }

        return false;
    }

    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs)
    {
        if (IsMouldGenerated)
            return false;

        // Right/middle click drive camera rotate/pan gestures; only place or select on left click.
        if (eventArgs.OriginalInputEventArgs is not System.Windows.Input.MouseButtonEventArgs { ChangedButton: System.Windows.Input.MouseButton.Left })
            return false;

        var hit = eventArgs.HitTestResult;

        if (hit?.ModelHit is not MeshGeometryModel3D meshHit)
        {
            // Missed everything: deselect rather than leaving a stale selection highlighted.
            SelectChannel(Guid.Empty);
            return false;
        }

        if (meshHit.GUID == _targetMeshId)
        {
            var point = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
            var normal = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);

            if (ActiveChannelType == AirChannelType.Painted)
            {
                // Painted channels are placed by dragging a stroke, not a single click;
                // the channel is committed on mouse-up.
                _strokePoints = [point];
                SetMouldHiddenForHover(true);
                StrokeUpdated?.Invoke(_strokePoints);
                return true;
            }

            ChannelPlaced?.Invoke(point, normal);
            return true;
        }

        if (_channelVisualToModelId.TryGetValue(meshHit.GUID, out var channelId))
        {
            SelectChannel(channelId);
            return true;
        }

        // Hit something unrelated (e.g. the grid): also clear the selection.
        SelectChannel(Guid.Empty);
        return false;
    }

    public bool OnMouseMove(HitTestResult? hit)
    {
        if (IsMouldGenerated)
            return false;

        bool overTarget = hit?.ModelHit is MeshGeometryModel3D meshHit && meshHit.GUID == _targetMeshId;

        if (StrokeActive)
        {
            // Safety net: if the release happened outside the viewport, MouseUp3D never
            // fired - commit on the first move back over the viewport.
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                CommitStroke();
                return true;
            }

            // Moves that miss the target mesh (drag slipped off the silhouette, or crossed
            // another visual) are skipped; the gap is bridged by a straight segment when
            // the stroke re-enters the mesh.
            if (overTarget)
            {
                var strokePoint = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
                if (Vector3.DistanceSquared(strokePoint, _strokePoints[^1]) >
                    MinStrokePointDistance * MinStrokePointDistance)
                {
                    _strokePoints.Add(strokePoint);
                    StrokeUpdated?.Invoke(_strokePoints);
                }
            }

            // No ChannelHovered here: the hover handler rebuilds a single-point preview
            // and would clobber the stroke preview.
            return true;
        }

        SetMouldHiddenForHover(overTarget);
        _mouseOverTarget = overTarget;

        if (!overTarget)
        {
            RenderPreviewChannel(); // hides the marker left over from the last hover
            return false;
        }

        var point = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
        var normal = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);

        // Total length depends on the mould's bounds and this point's own Z, so the
        // preview is rebuilt in full via ChannelHovered rather than just repositioned -
        // merely moving the start point would leave a stale total length.
        ChannelHovered?.Invoke(point, normal);

        return true;
    }

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs)
    {
        if (!StrokeActive)
            return false;

        // A right/middle release mid-stroke (camera gesture) must not end the stroke.
        if (eventArgs.OriginalInputEventArgs is not System.Windows.Input.MouseButtonEventArgs { ChangedButton: System.Windows.Input.MouseButton.Left })
            return false;

        // Commit uses the accumulated points, not the up-hit, so releasing off-mesh
        // still places the painted channel.
        CommitStroke();
        return true;
    }

    private void CommitStroke()
    {
        var points = _strokePoints!;
        _strokePoints = null;
        // Drop the cone drag-preview before the committed channel renders, or it lingers
        // on top of the new channel until the next mouse move rebuilds the hover preview.
        PreviewChannel = null;
        RenderPreviewChannel();
        StrokeCompleted?.Invoke(points);
    }

    private void CancelStroke()
    {
        _strokePoints = null;
        // The preview still holds the cancelled stroke's path; with the stroke no longer
        // active it would re-render in Full mode as a placed-looking solid.
        PreviewChannel = null;
        RenderPreviewChannel();
    }
}
