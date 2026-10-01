using BasicResults;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;
using System.Numerics;
using System.Windows.Threading;

namespace Fabolus.Wpf.Features.Moulding;

public partial class MouldViewModel : ObservableObject, IViewState
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly MouldSceneManager _sceneManager;
    private readonly GenerateMould _generateMouldFeature;
    private readonly ClearMould _clearMouldFeature;
    private readonly DetectAirPockets _detectAirPocketsFeature;

    private Workspace Workspace { get; set; }

    private List<AirChannelModel> Channels { get; set; } = [];
    public int ChannelCount => Channels.Count;

    // Stats of the mesh currently handed to the scene manager, cached so the hover path
    // (ComputeTotalLength runs on every mouse-move over the target) reads a value instead
    // of fetching a mesh and recomputing statistics per event.
    private MeshStatistics? _targetStats;

    // The mesh handed to the scene manager, kept for finding air pockets on. Immutable, so
    // sharing it with the scene manager is safe.
    private IMesh? _targetMesh;

    // The air pocket analysis of _targetMesh. Walking the mesh is the slow part and only the
    // mesh decides it, so it is done once per target; asking which pockets the channels leave
    // unvented is cheap and is asked again on every channel edit. Reset when the target changes.
    private Task<Result<AirPocketMap>>? _pocketMapTask;

    // Automatic placement works from the saved preferences rather than the panel, which may
    // be showing a selected channel's values.
    private PrintBedPreferences _printBed = PrintBedPreferences.Default;

    // What a channel starts out as before the user changes anything.
    private const float DefaultTipDiameter = 3.0f;
    private const float DefaultTipLength = 3.0f;
    private const float DefaultTipDepth = 1.0f;

    /// <summary>
    /// Rebuilding the mould shell costs about as much as the bolus is big - most of a second at a
    /// hundred thousand triangles. Every wall/base/trough slider rebuilds it, and a drag raises a
    /// change per step, so the rebuild is deferred until the user pauses rather than run per step.
    /// </summary>
    private readonly DispatcherTimer _mouldTimer;

    // A rebuild is wanted; set by any change, cleared when one starts.
    private bool _mouldPending;

    // A rebuild is in flight. A change arriving during one does not start a second: it re-arms
    // _mouldPending and the running rebuild goes round again, so however fast the parameters
    // change there is only ever one build running and one queued.
    private bool _mouldRebuilding;

    /// <summary>
    /// The viewport's loading overlay. The mould view had none at all, while being the slowest
    /// of the tools.
    /// </summary>
    private readonly BusyIndicator _busy;

    /// <summary>
    /// How many times the shell has actually been rebuilt. The coalescing is invisible from
    /// outside - a drag and a single step both raise the overlay once - so this is what lets a
    /// test tell "the change was folded into the running rebuild" from "the change was dropped".
    /// </summary>
    internal int MouldRebuildCount { get; private set; }

    // Last point/normal the mouse hovered on the target mesh, so switching channel type
    // rebuilds the preview in place instead of resetting it to the origin.
    private Vector3 _lastHoverPoint = Vector3.Zero;
    private Vector3 _lastHoverNormal = Vector3.UnitZ;

    // Selection lives in the scene manager (left-clicking a placed channel marker selects
    // it there); this mirrors that selection so the parameter panel can edit it.
    [ObservableProperty] private Guid _selectedChannelId = Guid.Empty;

    // The two sections of the tool rail behave as an accordion: opening one closes the
    // other, so the panel only ever shows one set of controls at a time. Both can still be
    // closed - collapsing the open section doesn't reopen its neighbour.
    [ObservableProperty] private bool _isChannelsExpanded = true;
    [ObservableProperty] private bool _isMouldExpanded;

    partial void OnIsChannelsExpandedChanged(bool value)
    {
        if (value) IsMouldExpanded = false;
    }

    partial void OnIsMouldExpandedChanged(bool value)
    {
        if (value) IsChannelsExpanded = false;
    }

    // True once Generate Mould has produced a result from the current settings/channels.
    // Drives the primary button (Generate <-> Clear) and gates the "settings changed"
    // guard below.
    [ObservableProperty] private bool _isGenerated;

    partial void OnIsGeneratedChanged(bool value)
    {
        _sceneManager.IsMouldGenerated = value;

        // The settings are baked into the result now, so both sections fold away and the
        // rail gets out of the way of the mould itself. Editing any of them clears the
        // mould first (see EnsureNotGenerated), so nothing here can be changed under it.
        if (value)
        {
            IsChannelsExpanded = false;
            IsMouldExpanded = false;
        }
    }

    // Once a mould has been generated, any further settings/channel edit invalidates it -
    // there's no incremental way to update baked-in geometry, so fall back to the
    // pre-generation mesh and let the edit proceed normally from there.
    private void EnsureNotGenerated()
    {
        if (IsGenerated)
            ClearGeneratedMould();
    }

    // True while the parameter fields are being populated *from* the selected channel,
    // so those assignments don't immediately turn around and rewrite the channel.
    private bool _syncingSelection;
    private bool _isActivating;

    partial void OnSelectedChannelIdChanged(Guid value)
    {
        var channel = value == Guid.Empty ? null : Channels.FirstOrDefault(c => c.Id == value);
        if (channel is null) return;

        _syncingSelection = true;
        ChannelType = channel.Type;
        TipLength = (float)channel.TipLength;
        var penetrationDepth = ExtractPenetrationDepth(channel.DomainModel);
        if (penetrationDepth.HasValue)
            TipDepth = penetrationDepth.Value;
        // Channel diameter first: tip diameter is clamped against it, so setting this
        // order avoids a transient clamp against a stale value from the prior selection.
        ChannelDiameter = (float)channel.ChannelDiameter;
        TipDiameter = (float)channel.TipDiameter;
        _syncingSelection = false;
    }

    private static float? ExtractPenetrationDepth(IAirChannel domainModel) => domainModel switch
    {
        StraightAirChannel s => s.PenetrationDepth,
        AngledAirChannel a => a.PenetrationDepth,
        PaintedAirChannel p => p.PenetrationDepth,
        _ => null
    };

    [ObservableProperty] private AirChannelType _channelType = AirChannelType.Straight;

    partial void OnChannelTypeChanged(AirChannelType value)
    {
        _sceneManager.ActiveChannelType = value;

        OnPropertyChanged(nameof(IsStraightType));
        OnPropertyChanged(nameof(IsAngledType));
        OnPropertyChanged(nameof(IsPathType));
        OnPropertyChanged(nameof(ChannelTypeDescription));

        ApplyChannelEdits();
    }

    [ObservableProperty] private float _tipDiameter = DefaultTipDiameter;
    [ObservableProperty] private float _tipLength = DefaultTipLength;
    [ObservableProperty] private float _tipDepth = DefaultTipDepth;
    [ObservableProperty] private float _channelDiameter = 5.0f;
    [ObservableProperty] private bool _autodetectChannels = true;

    // Markers on the bolus at each air pocket no channel vents yet.
    [ObservableProperty] private bool _showAirPockets = true;

    partial void OnShowAirPocketsChanged(bool value) => _ = RefreshAirPocketsAsync();

    partial void OnTipDiameterChanged(float value)
    {
        // The tip tapers down to the channel body, so it can never be wider than it.
        if (!_syncingSelection && value > ChannelDiameter)
        {
            TipDiameter = ChannelDiameter; // re-enters this handler with a valid value
            return;
        }

        ApplyChannelEdits();
    }

    partial void OnTipLengthChanged(float value) => ApplyChannelEdits();
    partial void OnTipDepthChanged(float value) => ApplyChannelEdits();

    partial void OnChannelDiameterChanged(float value)
    {
        if (!_syncingSelection && TipDiameter > value)
        {
            TipDiameter = value; // re-enters OnTipDiameterChanged, which applies the edits
            return;
        }

        ApplyChannelEdits();
    }

    // The hover preview always tracks current parameters (it's what the next click will
    // place); if a channel is also selected, it gets updated in place too.
    private void ApplyChannelEdits()
    {
        if (_syncingSelection) return;

        EnsureNotGenerated();
        UpdatePreviewChannel();

        if (SelectedChannelId != Guid.Empty)
            UpdateSelectedChannel();
    }

    private void UpdateSelectedChannel()
    {
        var existing = Channels.FirstOrDefault(c => c.Id == SelectedChannelId);
        if (existing is null) return;

        var position = existing.Position;
        var direction = existing.Direction;
        var totalLength = ComputeTotalLength((float)position.Z);

        IAirChannel domainModel = ChannelType switch
        {
            AirChannelType.Angled => new AngledAirChannel(position, direction, TipLength, totalLength, TipDiameter, ChannelDiameter / 2f, TipDepth),
            // Preserve the painted path when only parameters change; the single-point
            // fallback covers converting a Straight/Angled channel to Path (a disc
            // channel at its old position).
            AirChannelType.Painted => new PaintedAirChannel(
                existing.DomainModel is PaintedAirChannel painted ? painted.Path : [position],
                ChannelDiameter / 2f, totalLength, TipDepth),
            _ => new StraightAirChannel(position, TipLength, totalLength, TipDiameter, ChannelDiameter, TipDepth)
        };

        var updated = existing with
        {
            Type = ChannelType,
            TipDiameter = TipDiameter,
            ChannelDiameter = ChannelDiameter,
            TipLength = TipLength,
            DomainModel = domainModel
        };

        Channels = Channels.Select(c => c.Id == SelectedChannelId ? updated : c).ToList();

        _sceneManager.UpdateChannels(Channels);
        _ = RefreshAirPocketsAsync(); // turning a channel into a painted one changes what it vents
        UpdateMould();
    }

    public bool IsStraightType => ChannelType == AirChannelType.Straight;
    public bool IsAngledType => ChannelType == AirChannelType.Angled;
    public bool IsPathType => ChannelType == AirChannelType.Painted;

    public string ChannelTypeDescription => ChannelType switch
    {
        AirChannelType.Straight => "Drops straight down from the click point.",
        AirChannelType.Angled => "Follows the surface normal at the click point.",
        AirChannelType.Painted => "Hold the left mouse button and drag across the surface to paint a path; release to place the channel. Esc cancels.",
        _ => string.Empty
    };

    public IReadOnlyList<MouldShapeType> MouldShapeTypes { get; } = Enum.GetValues<MouldShapeType>();

    [ObservableProperty] private MouldShapeType _selectedMouldType = MouldShapeType.Concave;
    // "Wall thickness" maps to the XY offset around the mesh; "Base height" maps to the
    // vertical offset below/above the mesh bounds (both ends share the one slider).
    [ObservableProperty] private double _wallThickness = 2.0;
    [ObservableProperty] private double _baseHeight = 5.0;

    public IReadOnlyList<TroughShapeType> TroughShapeTypes { get; } = Enum.GetValues<TroughShapeType>();

    // The trough is the basin recessed into the top of the mould that excess silicone pools
    // in while it fills. Depth 0 means no trough - there's no separate toggle.
    [ObservableProperty] private double _troughHeight;
    [ObservableProperty] private double _troughOffset = 2.5;
    [ObservableProperty] private TroughShapeType _selectedTroughShape = TroughShapeType.Footprint;

    // ---- Slider bounds -------------------------------------------------
    // Bound by the view rather than hardcoded in it, so the range a value can be given here is
    // the same one its preference is validated against. The two used to be separate literals
    // that had drifted apart, which let a user pick a default the tool would not accept - or
    // set a value in the tool that no default could express.

    public double WallThicknessMinimum => MouldPreferences.Ranges.WallThicknessMin;
    public double WallThicknessMaximum => MouldPreferences.Ranges.WallThicknessMax;
    public double BaseHeightMinimum => MouldPreferences.Ranges.BaseHeightMin;
    public double BaseHeightMaximum => MouldPreferences.Ranges.BaseHeightMax;
    public double TroughDepthMinimum => MouldPreferences.Ranges.TroughHeightMin;
    public double TroughDepthMaximum => MouldPreferences.Ranges.TroughHeightMax;
    public double TroughMarginMinimum => MouldPreferences.Ranges.TroughOffsetMin;
    public double TroughMarginMaximum => MouldPreferences.Ranges.TroughOffsetMax;

    // The air channels live on this view model too, but their diameter is a print-bed preference.
    public double ChannelDiameterMinimum => PrintBedPreferences.Ranges.ChannelDiameterMin;
    public double ChannelDiameterMaximum => PrintBedPreferences.Ranges.ChannelDiameterMax;

    // A contoured mould follows the bolus surface, so it has no flat top to recess into.
    public bool SupportsTrough => SelectedMouldType != MouldShapeType.Contoured;
    public bool HasTrough => SupportsTrough && TroughHeight > 0;

    public string TroughOffsetHint => SelectedTroughShape == TroughShapeType.Channels
        ? "How far the pool spreads past the channel exits."
        : "Rim left standing between the pool and the mould wall.";

    /// <summary>
    /// How much silicone the trough holds, in millilitres. Measured alongside each shell rebuild,
    /// so it follows the depth and margin sliders as they are dragged.
    /// </summary>
    /// <remarks>
    /// Millilitres, not the cubic millimetres the geometry is measured in: this is a quantity of
    /// silicone somebody has to pour, and a basin of any useful size runs to five or six digits
    /// in mm3.
    /// </remarks>
    [ObservableProperty] private double _troughVolume;

    partial void OnTroughVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(HasTroughVolume));

        // Republished rather than bound, because the info panel takes a list rather than
        // individual properties - so a new capacity means sending the whole set again.
        PublishMeshInfo();
    }

    /// <summary>
    /// Whether there is a measured capacity worth showing. A trough whose parameters do not
    /// currently describe a basin - a margin that has eaten the whole footprint, say - measures
    /// nothing, and a blank row says more than "0.0 mL" would.
    /// </summary>
    public bool HasTroughVolume => HasTrough && TroughVolume > 0;

    partial void OnSelectedMouldTypeChanged(MouldShapeType value)
    {
        OnPropertyChanged(nameof(SupportsTrough));
        OnPropertyChanged(nameof(HasTrough));
        OnPropertyChanged(nameof(HasTroughVolume));
        UpdateMouldHeight();
    }

    // Wall thickness is what the contoured shape offsets its top by, so it moves the top of
    // the mould too.
    partial void OnWallThicknessChanged(double value) => UpdateMouldHeight();
    partial void OnBaseHeightChanged(double value) => UpdateMouldHeight();

    partial void OnTroughHeightChanged(double value)
    {
        OnPropertyChanged(nameof(HasTrough));
        OnPropertyChanged(nameof(HasTroughVolume));
        UpdateMouldHeight();
    }

    partial void OnTroughOffsetChanged(double value) => UpdateMould();

    partial void OnSelectedTroughShapeChanged(TroughShapeType value)
    {
        OnPropertyChanged(nameof(TroughOffsetHint));
        UpdateMould();
    }

    public ISceneManager SceneManager => _sceneManager;

    public MouldViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), GeometryEngine.BspGeometryEngine.Create()) { }
    public MouldViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine)
    {
        _messenger = messenger;
        _busy = new BusyIndicator(messenger);
        _alert = alert;
        _engine = engine;

        _generateMouldFeature = new GenerateMould(_engine);
        _clearMouldFeature = new ClearMould(_engine);
        _detectAirPocketsFeature = new DetectAirPockets(_engine);
        _sceneManager = new MouldSceneManager(_engine, _messenger);
        _sceneManager.ChannelPlaced += OnChannelPlaced;
        _sceneManager.ChannelSelected += id => SelectedChannelId = id;
        _sceneManager.ChannelHovered += (point, normal) =>
        {
            _lastHoverPoint = point;
            _lastHoverNormal = normal;
            UpdatePreviewChannel(); // rebuilds so the total length tracks this point's Z
        };
        _sceneManager.DeleteSelectedChannelRequested += DeleteSelectedChannel;
        _sceneManager.ActiveChannelType = ChannelType;
        _sceneManager.StrokeUpdated += OnStrokeUpdated;
        _sceneManager.StrokeCompleted += OnStrokeCompleted;

        _syncingSelection = true;
        ApplyPrintBedPreferences(_messenger.GetSection(PrintBedPreferences.Default));
        _syncingSelection = false;

        _messenger.Register<PreferenceSectionUpdateMessage<PrintBedPreferences>>(
            this, (r, m) => ApplyPrintBedPreferences(m.Section));

        // Long enough that a drag settles into one rebuild, short enough that letting go of a
        // slider feels like it answered. Restarted on every change, so it fires once the user
        // stops rather than repeatedly through the drag.
        _mouldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _mouldTimer.Tick += (_, _) =>
        {
            _mouldTimer.Stop();
            _ = RebuildMouldAsync();
        };
    }

    // Air-channel defaults are stored alongside the print bed; the panel mirrors them so a
    // change in preferences shows up here without reopening the view.
    private void ApplyPrintBedPreferences(PrintBedPreferences bed)
    {
        _printBed = bed;
        ChannelDiameter = bed.ChannelDiameter;
        AutodetectChannels = bed.AutodetectChannels;

        // The pocket depth and spacing decide which markers show.
        if (_targetMesh is not null)
            _ = RefreshAirPocketsAsync();
    }

    private void OnStrokeUpdated(IReadOnlyList<Vector3> points)
    {
        // Copy the list - the scene manager keeps mutating its accumulator.
        var path = points.ToList();
        var preview = new PaintedAirChannel(path, ChannelDiameter / 2f, ComputeTotalLength((float)path[0].Z), TipDepth);
        _sceneManager.UpdatePreviewChannel(preview);
    }

    private void OnStrokeCompleted(IReadOnlyList<Vector3> points)
    {
        EnsureNotGenerated();

        // Decimated raw input is still jittery; store the resampled/smoothed path so
        // persistence and every later regeneration work from the clean stroke.
        var resampleResult = _engine.Generators.ResampleOpenPath([.. points], spacing: 2.0f);
        var path = resampleResult.IsSuccess ? resampleResult.Value : points;

        var domainModel = new PaintedAirChannel(path, ChannelDiameter / 2f, ComputeTotalLength((float)path[0].Z), TipDepth);
        AddChannel(new AirChannelModel(Guid.NewGuid(), AirChannelType.Painted, TipDiameter, ChannelDiameter, TipLength, domainModel));
    }

    public async Task ActivateAsync(Workspace workspace)
    {
        _isActivating = true;
        try
        {
            await Task.Yield(); // Allow UI to render loading screen

            Workspace = workspace;

            var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure)
            return;

        // An owned copy; ownership transfers to the scene manager in SetSceneTarget below.
        IMesh mesh = activeMeshResult.Value;

        var recordResult = Workspace.GetActiveRecord();
        if (recordResult.IsFailure)
            return;

        var record = recordResult.Value;

        // MouldDefinition is only ever recorded on an actual generated-mould result (by
        // GenerateMould); PendingMould holds settings/channels the user was still editing
        // when they last left this mesh. Prefer the former - if this entry IS a mould, we're
        // viewing its baked result, not something still being edited.
        var mould = record.MouldDefinition();

        IsGenerated = mould is not null;

        // An entry that already carries a mould - baked or still being edited - reopens with its
        // own settings. Only one with neither falls back to the app preferences.
        var mouldDefinition = mould
            ?? record.PendingMould
            ?? BuildPreferredMouldDefinition();

        SelectedChannelId = Guid.Empty;
        Channels = mouldDefinition.AirChannels.ToList();
        OnPropertyChanged(nameof(ChannelCount));

        SelectedMouldType = mouldDefinition switch
        {
            ConvexMouldDefinition => MouldShapeType.Convex,
            ContouredMouldDefinition => MouldShapeType.Contoured,
            _ => MouldShapeType.Concave
        };

        (WallThickness, BaseHeight) = mouldDefinition switch
        {
            ConvexMouldDefinition c => (c.OffsetXY, c.OffsetBottom),
            ConcaveMouldDefinition c => (c.OffsetXY, c.OffsetBottom),
            ContouredMouldDefinition c => (c.OffsetXY, BaseHeight),
            _ => (WallThickness, BaseHeight)
        };

        TroughHeight = mouldDefinition.TroughHeight;
        TroughOffset = mouldDefinition.TroughOffset;
        SelectedTroughShape = mouldDefinition.TroughShape;

        UpdatePreviewChannel();

        // The scene manager only ever renders the active mesh, so that's all it gets -
        // the Workspace itself stays here in the view model.
        SetSceneTarget(mesh);

        // With the preference on, a mesh that arrives here with no channels gets them placed at
        // its air pockets - on every entry, so clearing them all and coming back places them
        // again. Done before the shell is first built so it is cut with them.
        if (!IsGenerated && Channels.Count == 0 && AutodetectChannels)
        {
            var placed = await FindPocketChannelsAsync();
            if (placed.IsFailure)
                _alert.ShowError(placed.Error.Description);
            else
                Channels = [.. placed.Value];

            OnPropertyChanged(nameof(ChannelCount));
        }

        if (!IsGenerated)
        {
            _sceneManager.UpdateChannels(Channels);

        }
        else
        {
            _sceneManager.ClearPreviews();
        }

        // Before the shell is first built, so the markers sit ahead of it in the scene.
        await RefreshAirPocketsAsync();
        }
        finally
        {
            _isActivating = false;
        }

        if (!IsGenerated)
        {
            // Built now rather than through the debounce timer: opening the view should show the
            // shell when it is ready, not a beat later. MainViewModel holds the overlay up over
            // this, and the build itself is off the UI thread, so it can finally paint.
            _mouldPending = true;
            await RebuildMouldAsync();
        }
        else
        {
            // Nothing is rebuilt for a mould that is already baked, so its trough would otherwise
            // go unmeasured and the panel would report none for a mould that plainly has one.
            await MeasureGeneratedTroughAsync();
        }
    }

    /// <summary>
    /// Measures the trough of an already-generated mould, for the panel to report on re-entry.
    /// </summary>
    /// <remarks>
    /// The mould is the active mesh by this point, and the trough was cut from the bolus's
    /// footprint rather than the mould's - measuring against the mould would inset an outline
    /// that has already been grown by the wall thickness, giving a rim that never existed. The
    /// mesh is rewound to the stage the mould was generated from instead: everything below
    /// <see cref="CommandPriority.Mould"/> replayed, which is exactly what the definition was
    /// handed when it built the shell.
    /// </remarks>
    private async Task MeasureGeneratedTroughAsync()
    {
        var meshResult = Workspace.GetActiveMesh();
        var recordResult = Workspace.GetActiveRecord();
        if (meshResult.IsFailure || recordResult.IsFailure) return;

        var record = recordResult.Value;
        if (record.MouldDefinition() is not { } definition) return;

        var sourceResult = CommandReplay.GetMeshAtStage(
            _engine, meshResult.Value, record, CommandPriority.TextEmboss);
        if (sourceResult.IsFailure) return;

        var source = sourceResult.Value;
        var capacity = await Task.Run(() => definition.TroughCapacity(_engine, source));

        TroughVolume = capacity.IsSuccess ? Measure.ToMillilitres(capacity.Value) : 0.0;
    }

    public Task<Workspace> DeactivateAsync()
    {
        // Nothing to rebuild for a view that is going away, and the scene manager is about to
        // lose the mesh a queued rebuild would read.
        _mouldPending = false;
        _mouldTimer.Stop();

        PersistUncommittedMouldState();
        _sceneManager.ReleaseMesh();
        _targetMesh = null;
        _pocketMapTask = null;
        return Task.FromResult(Workspace);
    }

    // Hands the mesh to the scene manager (which takes ownership of it) and caches the
    // stats the hover path needs.
    private void SetSceneTarget(IMesh mesh)
    {
        _targetMesh = mesh;
        _pocketMapTask = null;
        _targetStats = mesh.Stats();
        PublishMeshInfo();

        var result = _sceneManager.UpdateMesh(mesh);
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);
    }

    /// <summary>
    /// Fills the viewport's info panel.
    /// </summary>
    /// <remarks>
    /// The mould view was the one tool that published nothing to it, so the panel sat empty here
    /// while every other view filled it. MainViewModel clears the panel as the view changes and
    /// this runs from ActivateAsync afterwards, so the order works out without any coordination
    /// between the two.
    /// </remarks>
    private void PublishMeshInfo()
    {
        var items = new List<MeshInfoItem>();

        if (_targetStats is not null)
        {
            // Once generated, the active mesh IS the mould, so from then on these describe the
            // mould itself rather than the bolus it was built around.
            items.Add(new TitleInfoItem { Label = IsGenerated ? "MOULD STATISTICS" : "MESH STATISTICS" });
            items.Add(new TextInfoItem { Label = "Triangles", Value = _targetStats.TriangleCount.ToString("N0") });
            items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{Measure.ToSquareCentimetres(_targetStats.SurfaceArea):F2} cm²" });

            // Named for what it is measuring rather than just "Volume", so it cannot be mistaken
            // for the trough's. Which mesh that is changes once the mould is generated: from then
            // on the active mesh is the mould, not the bolus it was built around.
            items.Add(new TextInfoItem {
                Label = IsGenerated ? "Mould Volume" : "Bolus Volume",
                Value = $"{Measure.ToMillilitres(_targetStats.Volume):F2} mL"
            });

            var size = _targetStats.BoundsSize;
            items.Add(new TextInfoItem { Label = "Dimensions", Value = $"{size.X:F1} x {size.Y:F1} x {size.Z:F1} mm" });
        }

        // One line among the rest rather than a section of its own. Left out entirely when the
        // settings carve no basin, rather than shown as a zero.
        if (HasTroughVolume)
        {
            items.Add(new TextInfoItem { Label = "Trough Volume", Value = $"{TroughVolume:F1} mL" });
        }

        _messenger.Send(new UpdateMeshInfoMessage(items));
    }

    // The mould/channel settings only live here in the ViewModel until Generate is
    // clicked. If the user switches away (and another feature - Smooth, Rotate, etc. -
    // then updates this mesh), that in-progress work would otherwise be lost. Saved as
    // PendingMould on the workspace entry, distinct from a recorded MouldDefinition (which
    // means "this entry IS a generated mould"); the record outlives any change to the
    // entry's geometry, so this is enough.
    private void PersistUncommittedMouldState()
    {
        // Already generated: GenerateMould recorded the definition on the entry - nothing
        // pending to persist for this (no-longer-active) mesh.
        if (IsGenerated || Channels.Count == 0)
            return;

        var recordResult = Workspace.GetActiveRecord();
        if (recordResult.IsFailure)
            return;

        var result = Workspace.UpdateRecord(recordResult.Value.WithPendingMould(BuildMouldDefinition()));
        if (result.IsSuccess)
            Workspace = result.Value;
    }

    private MouldDefinition BuildPreferredMouldDefinition()
    {
        return _messenger.GetSection(MouldPreferences.Default).Clamped().ToMouldDefinition();
    }

    private MouldDefinition BuildMouldDefinition()
    {
        // The trough settings ride along even on a contoured mould (which ignores them), so
        // switching shape and back doesn't lose what the user had dialled in.
        return MouldDefinition.OfShape(SelectedMouldType, WallThickness, BaseHeight) with
        {
            AirChannels = Channels,
            TroughHeight = TroughHeight,
            TroughOffset = TroughOffset,
            TroughShape = SelectedTroughShape
        };
    }

    // Every channel is cut to vent just past the top of the mould, and its length is baked
    // in when it's placed. Anything that moves that top - the shape, the base height, the
    // trough's depth - has to re-cut the channels already standing, or they finish below the
    // new top and get sealed in (into the trough's pool, in the case that raises the top
    // furthest) instead of venting out of it.
    private void UpdateMouldHeight()
    {
        if (_isActivating) return;

        EnsureNotGenerated();
        UpdateChannelLengths();
        UpdateMould();
    }

    private void UpdateChannelLengths()
    {
        if (Channels.Count == 0) return;

        Channels = Channels
            .Select(channel => channel with { DomainModel = Relengthen(channel.DomainModel) })
            .ToList();

        _sceneManager.UpdateChannels(Channels);
    }

    private IAirChannel Relengthen(IAirChannel channel) => channel switch
    {
        StraightAirChannel s => s with { TotalLength = ComputeTotalLength((float)s.StartPoint.Z) },
        AngledAirChannel a => a with { TotalLength = ComputeTotalLength((float)a.StartPoint.Z) },
        // A painted channel is extruded from the height its stroke started at.
        PaintedAirChannel { Path.Count: > 0 } p => p with { TotalLength = ComputeTotalLength((float)p.Path[0].Z) },
        _ => channel
    };

    /// <summary>
    /// Asks for the mould shell to be rebuilt once the parameters stop changing. Returns at once;
    /// the rebuild happens off the UI thread behind the loading overlay.
    /// </summary>
    private void UpdateMould()
    {
        if (_isActivating) return;

        EnsureNotGenerated();

        _mouldPending = true;
        _mouldTimer.Stop();
        _mouldTimer.Start();
    }

    /// <summary>
    /// Rebuilds the mould shell and puts it on screen. Building is the slow part and runs off the
    /// UI thread; showing the result happens back on it, because the continuation is awaited here.
    /// </summary>
    /// <remarks>
    /// Loops rather than recursing so that changes arriving mid-build are absorbed into one more
    /// pass: at most one build runs and one is queued, however fast the user drags. The overlay is
    /// taken once around the whole thing rather than per pass, so it does not flicker between them.
    /// </remarks>
    private async Task RebuildMouldAsync()
    {
        if (_mouldRebuilding)
        {
            // One is already running; make sure it goes round again with the newer parameters.
            _mouldPending = true;
            return;
        }

        if (!_mouldPending) return;

        _mouldRebuilding = true;
        using var busy = _busy.Enter();
        try
        {
            while (_mouldPending)
            {
                _mouldPending = false;
                MouldRebuildCount++;

                var definition = BuildMouldDefinition();

                // Measured in the same pass as the shell, off the UI thread, so the figure on
                // screen always describes the mould on screen rather than lagging a rebuild
                // behind it.
                var (built, capacity) = await Task.Run(() => (
                    _sceneManager.BuildMould(definition),
                    _sceneManager.MeasureTroughCapacity(definition)));

                // Parameters that do not describe a mould come back empty, which is routine
                // part-way through a drag - the shell is cleared rather than reported.
                _sceneManager.ShowMould(built);

                TroughVolume = capacity.HasValue ? Measure.ToMillilitres(capacity.Value) : 0.0;
            }
        }
        finally
        {
            _mouldRebuilding = false;
        }
    }

    // The channel must vent above the mould, not stay sealed inside it: its top always
    // ends 2.0mm above the mould's bounding box, regardless of where its base is placed.
    private const float MouldClearance = 2.0f;


    private float ComputeTotalLength(float startZ)
    {
        // Runs on every mouse-move over the target mesh - reads the stats cached in
        // SetSceneTarget instead of fetching a mesh copy and recomputing per event.
        if (_targetStats is null)
            return TipLength;

        var topOffset = SelectedMouldType == MouldShapeType.Contoured ? WallThickness : BaseHeight;
        // A trough raises the top of the mould by its depth, and the channel still has to
        // vent above the rim rather than into the pool.
        var mouldTopZ = _targetStats.BoundsMax.Z + topOffset + (HasTrough ? TroughHeight : 0.0);
        var totalLength = (float)(mouldTopZ + MouldClearance) - startZ;

        // Never let the total length come out shorter than the cone/tip itself.
        return Math.Max(totalLength, TipLength);
    }

    private void UpdatePreviewChannel()
    {
        var point = _lastHoverPoint;
        var normal = _lastHoverNormal;
        var totalLength = ComputeTotalLength((float)point.Z);

        IAirChannel preview = ChannelType switch
        {
            AirChannelType.Angled => new AngledAirChannel(point, normal, TipLength, totalLength, TipDiameter, ChannelDiameter / 2f, TipDepth),
            AirChannelType.Painted => new PaintedAirChannel([point], ChannelDiameter / 2f, totalLength, TipDepth),
            _ => new StraightAirChannel(point, TipLength, totalLength, TipDiameter, ChannelDiameter, TipDepth)
        };

        _sceneManager.UpdatePreviewChannel(preview);
    }

    // Painted channels never arrive here - the scene manager routes their left-clicks
    // into a paint stroke, committed via OnStrokeCompleted.
    private void OnChannelPlaced(Vector3 point, Vector3 normal)
    {
        var totalLength = ComputeTotalLength((float)point.Z);

        IAirChannel domainModel = ChannelType switch
        {
            AirChannelType.Angled => new AngledAirChannel(point, normal, TipLength, totalLength, TipDiameter, ChannelDiameter / 2f, TipDepth),
            _ => new StraightAirChannel(point, TipLength, totalLength, TipDiameter, ChannelDiameter, TipDepth)
        };

        AddChannel(new AirChannelModel(Guid.NewGuid(), ChannelType, TipDiameter, ChannelDiameter, TipLength, domainModel));
    }

    private void AddChannel(AirChannelModel channel)
    {
        Channels = [.. Channels, channel];
        OnPropertyChanged(nameof(ChannelCount));

        _sceneManager.UpdateChannels(Channels);
        _sceneManager.SelectChannel(channel.Id);
        _ = RefreshAirPocketsAsync();
        UpdateMould();
    }

    /// <summary>
    /// Adds a channel at every air pocket no channel vents yet. The channels already placed are
    /// left alone, and each new one can be selected and edited like one placed by hand.
    /// </summary>
    [RelayCommand]
    public async Task AutoPlaceChannelsAsync()
    {
        EnsureNotGenerated();

        Result<IReadOnlyList<AirChannelModel>> placed;
        using (_busy.Enter())
        {
            placed = await FindPocketChannelsAsync();
        }

        if (placed.IsFailure)
        {
            _alert.ShowError(placed.Error.Description);
            return;
        }

        if (placed.Value.Count == 0)
        {
            _alert.ShowInfo("No air pockets were found that aren't already vented by a channel.");
            return;
        }

        Channels = [.. Channels, .. placed.Value];
        OnPropertyChanged(nameof(ChannelCount));

        _sceneManager.UpdateChannels(Channels);
        _ = RefreshAirPocketsAsync();
        UpdateMould();
    }

    /// <summary>
    /// An angled channel for each air pocket in the target that the current channels do not
    /// already vent, built to the preference defaults rather than the panel's values.
    /// </summary>
    private async Task<Result<IReadOnlyList<AirChannelModel>>> FindPocketChannelsAsync()
    {
        var pockets = await UnventedAirPocketsAsync();
        if (pockets.IsFailure)
            return pockets.Error;

        var diameter = _printBed.ChannelDiameter;
        var tipDiameter = Math.Min(DefaultTipDiameter, diameter);

        var channels = pockets.Value
            .Select(pocket => new AirChannelModel(
                Guid.NewGuid(), AirChannelType.Angled, tipDiameter, diameter, DefaultTipLength,
                new AngledAirChannel(
                    pocket.Point, pocket.Normal, DefaultTipLength, ComputeTotalLength((float)pocket.Point.Z),
                    tipDiameter, diameter / 2f, DefaultTipDepth)))
            .ToList();

        return Result<IReadOnlyList<AirChannelModel>>.Success(channels);
    }

    /// <summary>
    /// The air pockets in the target that no current channel vents, under the preference
    /// settings. Analyses the target on first use, off the UI thread; quick after that.
    /// </summary>
    private async Task<Result<IReadOnlyList<AirPocket>>> UnventedAirPocketsAsync()
    {
        var mesh = _targetMesh;
        if (mesh is null)
            return Result<IReadOnlyList<AirPocket>>.Success(Array.Empty<AirPocket>());

        // A pass over every vertex - quick, but not something to hold the UI thread for on a
        // large bolus, and the same mesh is only ever analysed once.
        _pocketMapTask ??= Task.Run(() => _detectAirPocketsFeature.Analyze(mesh));
        var map = await _pocketMapTask;
        if (map.IsFailure)
            return map.Error;

        var settings = new AirPocketSettings(_printBed.PocketDepth, _printBed.ChannelSpacing);
        return map.Value.Unvented(settings, Channels.SelectMany(VentPoints).ToList());
    }

    /// <summary>
    /// Puts a marker on each air pocket the channels leave unvented - the places Auto-place
    /// would add a channel. Hidden while the toggle is off or once the mould is generated.
    /// </summary>
    private async Task RefreshAirPocketsAsync()
    {
        if (IsGenerated || !ShowAirPockets || _targetMesh is null)
        {
            ShowAirPocketMarkers([]);
            return;
        }

        var target = _targetMesh;
        var pockets = await UnventedAirPocketsAsync();

        // The first analysis of a mesh is awaited, and in that time the view can have moved on.
        if (target != _targetMesh || IsGenerated || !ShowAirPockets)
            return;

        ShowAirPocketMarkers(pockets.IsSuccess
            ? pockets.Value.Select(p => p.Point).ToList()
            : []);
    }

    private void ShowAirPocketMarkers(IReadOnlyList<Vector3> points)
    {
        AirPocketMarkerCount = points.Count;
        _sceneManager.ShowAirPockets(points);
    }

    /// <summary>How many air pocket markers are on screen, for tests to read.</summary>
    internal int AirPocketMarkerCount { get; private set; }

    // Where a channel meets the surface - all along its path, for a painted one.
    private static IEnumerable<Vector3> VentPoints(AirChannelModel channel) => channel.DomainModel switch
    {
        PaintedAirChannel painted => painted.Path,
        _ => [channel.Position]
    };

    [RelayCommand]
    public void SetChannelType(string channelType)
    {
        ChannelType = channelType switch
        {
            "Straight" => AirChannelType.Straight,
            "Angled" => AirChannelType.Angled,
            "Path" => AirChannelType.Painted,
            _ => throw new Exception($"{channelType} doesn't match any AirChannelType")
        };
    }

    [RelayCommand]
    public void DeleteSelectedChannel()
    {
        if (SelectedChannelId == Guid.Empty) return;

        EnsureNotGenerated();
        Channels = Channels.Where(c => c.Id != SelectedChannelId).ToList();
        OnPropertyChanged(nameof(ChannelCount));

        _sceneManager.UpdateChannels(Channels);
        _sceneManager.SelectChannel(Guid.Empty);
        _ = RefreshAirPocketsAsync();
        UpdateMould();
    }

    [RelayCommand]
    public void ClearChannels()
    {
        EnsureNotGenerated();
        Channels = [];
        OnPropertyChanged(nameof(ChannelCount));

        _sceneManager.UpdateChannels(Channels);
        _sceneManager.SelectChannel(Guid.Empty);
        _ = RefreshAirPocketsAsync();
        UpdateMould();
    }

    [RelayCommand]
    public async Task GenerateMouldAsync()
    {
        using var busy = _busy.Enter();

        var mouldDefinition = BuildMouldDefinition();

        // Cutting the mould for real costs more than previewing it, and the preview alone was
        // most of a second on a large bolus.
        var workspace = Workspace;
        var activeId = Workspace.ActiveMeshId;
        var result = await Task.Run(() => _generateMouldFeature.Execute(workspace, activeId, mouldDefinition));
        if (result.IsFailure)
        {
            _alert.ShowError(result.Error.Description);
            return;
        }

        Workspace = result.Value;
        IsGenerated = true;

        // The mould shell and channels are now baked into the generated mesh itself
        // (and saved on its metadata by GenerateMould) - drop the pre-generation
        // overlays, but keep Channels/settings in memory so Clear can restore them.
        _sceneManager.ClearPreviews();

        var meshResult = Workspace.GetActiveMesh();
        if (meshResult.IsSuccess)
            SetSceneTarget(meshResult.Value);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }

    // Left synchronous: EnsureNotGenerated calls this from the parameter-change handlers, which
    // have to see the revert finished before they go on to rebuild against the reverted mesh.
    [RelayCommand]
    public void ClearGeneratedMould()
    {
        if (!IsGenerated) return;

        using var busy = _busy.Enter();

        var result = _clearMouldFeature.Execute(Workspace);
        if (result.IsFailure)
        {
            _alert.ShowError(result.Error.Description);
            return;
        }

        Workspace = result.Value;
        IsGenerated = false;

        var meshResult = Workspace.GetActiveMesh();
        if (meshResult.IsSuccess)
            SetSceneTarget(meshResult.Value);

        _sceneManager.UpdateChannels(Channels);
        if (SelectedChannelId != Guid.Empty)
            _sceneManager.SelectChannel(SelectedChannelId);
        _ = RefreshAirPocketsAsync();
        UpdateMould();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }
}
