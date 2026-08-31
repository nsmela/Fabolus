using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Viewport;
using System.Numerics;

namespace Fabolus.Wpf.Features.Moulding;

public partial class MouldViewModel : ObservableObject, IViewState
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly MouldSceneManager _sceneManager;
    private readonly GenerateMould _generateMouldFeature;
    private readonly ClearMould _clearMouldFeature;

    private Workspace Workspace { get; set; }

    private List<AirChannelModel> Channels { get; set; } = [];
    public int ChannelCount => Channels.Count;

    // Stats of the mesh currently handed to the scene manager, cached so the hover path
    // (ComputeTotalLength runs on every mouse-move over the target) reads a value instead
    // of fetching a mesh and recomputing statistics per event.
    private MeshStatistics? _targetStats;

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

    [ObservableProperty] private float _tipDiameter = 3.0f;
    [ObservableProperty] private float _tipLength = 3.0f;
    [ObservableProperty] private float _tipDepth = 1.0f;
    [ObservableProperty] private float _channelDiameter = 5.0f;
    [ObservableProperty] private bool _autodetectChannels = true;

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
        var totalLength = ComputeTotalLength(position.Z);

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

    partial void OnSelectedMouldTypeChanged(MouldShapeType value)
    {
        OnPropertyChanged(nameof(SupportsTrough));
        OnPropertyChanged(nameof(HasTrough));
        UpdateMouldHeight();
    }

    // Wall thickness is what the contoured shape offsets its top by, so it moves the top of
    // the mould too.
    partial void OnWallThicknessChanged(double value) => UpdateMouldHeight();
    partial void OnBaseHeightChanged(double value) => UpdateMouldHeight();

    partial void OnTroughHeightChanged(double value)
    {
        OnPropertyChanged(nameof(HasTrough));
        UpdateMouldHeight();
    }

    partial void OnTroughOffsetChanged(double value) => UpdateMould();

    partial void OnSelectedTroughShapeChanged(TroughShapeType value)
    {
        OnPropertyChanged(nameof(TroughOffsetHint));
        UpdateMould();
    }

    public ISceneManager SceneManager => _sceneManager;

    public MouldViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), new GeometryMeshLib.GeometryEngine(new FileSystem())) { }
    public MouldViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;

        _generateMouldFeature = new GenerateMould(_engine);
        _clearMouldFeature = new ClearMould(_engine);
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
    }

    // Air-channel defaults are stored alongside the print bed; the panel mirrors them so a
    // change in preferences shows up here without reopening the view.
    private void ApplyPrintBedPreferences(PrintBedPreferences bed)
    {
        ChannelDiameter = bed.ChannelDiameter;
        AutodetectChannels = bed.AutodetectChannels;
    }

    private void OnStrokeUpdated(IReadOnlyList<Vector3> points)
    {
        // Copy the list - the scene manager keeps mutating its accumulator.
        var path = points.ToList();
        var preview = new PaintedAirChannel(path, ChannelDiameter / 2f, ComputeTotalLength(path[0].Z), TipDepth);
        _sceneManager.UpdatePreviewChannel(preview);
    }

    private void OnStrokeCompleted(IReadOnlyList<Vector3> points)
    {
        EnsureNotGenerated();

        // Decimated raw input is still jittery; store the resampled/smoothed path so
        // persistence and every later regeneration work from the clean stroke.
        var resampleResult = _engine.Generators.ResampleOpenPath(points, targetSpacing: 2.0f);
        var path = resampleResult.IsSuccess ? resampleResult.Value : points;

        var domainModel = new PaintedAirChannel(path, ChannelDiameter / 2f, ComputeTotalLength(path[0].Z), TipDepth);
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

        // MouldDefinition is only ever set on an actual generated-mould result (by
        // GenerateMould); PendingMouldDefinition holds settings/channels the user was
        // still editing when they last left this mesh. Prefer the former - if this mesh
        // IS a mould, we're viewing its baked result, not something still being edited.
        var mouldResult = mesh.Metadata.MouldDefinition();

        IsGenerated = mouldResult.HasValue;

        // A mesh that already carries a mould - baked or still being edited - reopens with its
        // own settings. Only a mesh with neither falls back to the app preferences.
        var mouldDefinition = mouldResult.HasValue
            ? mouldResult.Value
            : mesh.Metadata.PendingMouldDefinition().GetValueOrDefault(BuildPreferredMouldDefinition());

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

        if (!IsGenerated)
        {
            _sceneManager.UpdateChannels(Channels);

        }
        else
        {
            _sceneManager.ClearPreviews();
        }
        }
        finally
        {
            _isActivating = false;
        }
        if (!IsGenerated)
        {
            UpdateMould();
        }
    }

    public Task<Workspace> DeactivateAsync()
    {
        PersistUncommittedMouldState();
        _sceneManager.ReleaseMesh();
        return Task.FromResult(Workspace);
    }

    // Hands the mesh to the scene manager (which takes ownership of it) and caches the
    // stats the hover path needs.
    private void SetSceneTarget(IMesh mesh)
    {
        var statsResult = mesh.Metadata.MeshStats();
        _targetStats = statsResult.HasValue ? statsResult.Value : null;

        var result = _sceneManager.UpdateMesh(mesh);
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);
    }

    // The mould/channel settings only live here in the ViewModel until Generate is
    // clicked. If the user switches away (and another feature - Smooth, Rotate, etc. -
    // then forks or updates this mesh), that in-progress work would otherwise be lost.
    // Saved as PendingMouldDefinition, distinct from MouldDefinition (which means "this
    // mesh IS a generated mould") - metadata already carries forward across those forks
    // (they copy the existing metadata and only touch their own keys), so this is enough.
    private void PersistUncommittedMouldState()
    {
        // Already generated: GenerateMould saved the correct metadata directly on the
        // result mesh - nothing pending to persist for this (no-longer-active) mesh.
        if (IsGenerated || Channels.Count == 0)
            return;

        var meshResult = Workspace.GetActiveMesh();
        if (meshResult.IsFailure)
            return;

        // WithMetadata transfers native ownership from the fetched copy to updatedMesh,
        // and UpdateMesh consumes updatedMesh - nothing left to dispose on success.
        var mesh = meshResult.Value;
        var updatedMesh = mesh.WithMetadata(mesh.Metadata.WithPendingMouldDefinition(BuildMouldDefinition()));

        var result = Workspace.UpdateMesh(updatedMesh);
        if (result.IsSuccess)
            Workspace = result.Value;
    }

    private MouldDefinition BuildPreferredMouldDefinition()
    {
        MouldPreferences prefs;
        try
        {
            prefs = _messenger.Send(new PreferenceSectionRequestMessage<MouldPreferences>()).Response
                ?? MouldPreferences.Default;
        }
        catch
        {
            prefs = MouldPreferences.Default;
        }

        return prefs.Clamped().ToMouldDefinition();
    }

    private MouldDefinition BuildMouldDefinition()
    {
        MouldDefinition definition = SelectedMouldType switch
        {
            MouldShapeType.Convex => new ConvexMouldDefinition(WallThickness, BaseHeight, BaseHeight),
            MouldShapeType.Contoured => new ContouredMouldDefinition(WallThickness),
            _ => new ConcaveMouldDefinition(WallThickness, BaseHeight, BaseHeight)
        };

        // The trough settings ride along even on a contoured mould (which ignores them), so
        // switching shape and back doesn't lose what the user had dialled in.
        return definition with
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
        StraightAirChannel s => s with { TotalLength = ComputeTotalLength(s.StartPoint.Z) },
        AngledAirChannel a => a with { TotalLength = ComputeTotalLength(a.StartPoint.Z) },
        // A painted channel is extruded from the height its stroke started at.
        PaintedAirChannel { Path.Count: > 0 } p => p with { TotalLength = ComputeTotalLength(p.Path[0].Z) },
        _ => channel
    };

    private void UpdateMould()
    {
        if (_isActivating) return;

        EnsureNotGenerated();

        var result = _sceneManager.UpdateMould(BuildMouldDefinition());
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);
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
        var mouldTopZ = _targetStats.MaxZ + topOffset + (HasTrough ? TroughHeight : 0.0);
        var totalLength = (float)(mouldTopZ + MouldClearance) - startZ;

        // Never let the total length come out shorter than the cone/tip itself.
        return Math.Max(totalLength, TipLength);
    }

    private void UpdatePreviewChannel()
    {
        var point = _lastHoverPoint;
        var normal = _lastHoverNormal;
        var totalLength = ComputeTotalLength(point.Z);

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
        var totalLength = ComputeTotalLength(point.Z);

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
        UpdateMould();
    }

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
        UpdateMould();
    }

    [RelayCommand]
    public void GenerateMould()
    {
        var mouldDefinition = BuildMouldDefinition();

        var result = _generateMouldFeature.Execute(Workspace, Workspace.ActiveMeshId, mouldDefinition);
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

    [RelayCommand]
    public void ClearGeneratedMould()
    {
        if (!IsGenerated) return;

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
        UpdateMould();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }
}
