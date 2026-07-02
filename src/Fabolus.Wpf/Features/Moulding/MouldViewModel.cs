using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
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

    // Last point/normal the mouse hovered on the target mesh, so switching channel type
    // rebuilds the preview in place instead of resetting it to the origin.
    private Vector3 _lastHoverPoint = Vector3.Zero;
    private Vector3 _lastHoverNormal = Vector3.UnitZ;

    // Selection lives in the scene manager (left-clicking a placed channel marker selects
    // it there); this mirrors that selection so the parameter panel can edit it.
    [ObservableProperty] private Guid _selectedChannelId = Guid.Empty;

    // True once Generate Mould has produced a result from the current settings/channels.
    // Drives the primary button (Generate <-> Clear) and gates the "settings changed"
    // guard below.
    [ObservableProperty] private bool _isGenerated;

    partial void OnIsGeneratedChanged(bool value) => _sceneManager.IsMouldGenerated = value;

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
            AirChannelType.Painted => new PaintedAirChannel([position], ChannelDiameter / 2f, totalLength, TipDepth),
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
        AirChannelType.Painted => "Traces a path painted across the surface.",
        _ => string.Empty
    };

    public IReadOnlyList<MouldShapeType> MouldShapeTypes { get; } = Enum.GetValues<MouldShapeType>();

    [ObservableProperty] private MouldShapeType _selectedMouldType = MouldShapeType.Concave;
    // "Wall thickness" maps to the XY offset around the mesh; "Base height" maps to the
    // vertical offset below/above the mesh bounds (both ends share the one slider).
    [ObservableProperty] private double _wallThickness = 2.0;
    [ObservableProperty] private double _baseHeight = 5.0;

    partial void OnSelectedMouldTypeChanged(MouldShapeType value) => UpdateMould();
    partial void OnWallThicknessChanged(double value) => UpdateMould();
    partial void OnBaseHeightChanged(double value) => UpdateMould();

    public ISceneManager SceneManager => _sceneManager;

    public MouldViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), new GeometryMeshLib.GeometryEngine(new FileSystem())) { }
    public MouldViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;

        _generateMouldFeature = new GenerateMould(_engine);
        _clearMouldFeature = new ClearMould(_engine);
        _sceneManager = new MouldSceneManager(_engine);
        _sceneManager.ChannelPlaced += OnChannelPlaced;
        _sceneManager.ChannelSelected += id => SelectedChannelId = id;
        _sceneManager.ChannelHovered += (point, normal) =>
        {
            _lastHoverPoint = point;
            _lastHoverNormal = normal;
            UpdatePreviewChannel(); // rebuilds so the total length tracks this point's Z
        };
        _sceneManager.DeleteSelectedChannelRequested += DeleteSelectedChannel;
    }

    public void Activate(Workspace workspace)
    {
        Workspace = workspace;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure)
            return;

        IMesh mesh = activeMeshResult.Value;

        // MouldDefinition is only ever set on an actual generated-mould result (by
        // GenerateMould); PendingMouldDefinition holds settings/channels the user was
        // still editing when they last left this mesh. Prefer the former - if this mesh
        // IS a mould, we're viewing its baked result, not something still being edited.
        var mouldResult = mesh.Metadata.MouldDefinition();

        IsGenerated = mouldResult.HasValue;

        var mouldDefinition = mouldResult.HasValue
            ? mouldResult.Value
            : mesh.Metadata.PendingMouldDefinition().GetValueOrDefault(new ConcaveMouldDefinition());

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

        UpdatePreviewChannel();

        // The scene manager only ever renders the active mesh, so that's all it gets -
        // the Workspace itself stays here in the view model.
        var result = _sceneManager.UpdateMesh(mesh);
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);

        _sceneManager.UpdateChannels(Channels);
        UpdateMould();
    }

    public Workspace Deactivate()
    {
        PersistUncommittedMouldState();
        return Workspace;
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

        var mesh = meshResult.Value;
        var updatedMesh = mesh.WithMetadata(mesh.Metadata.WithPendingMouldDefinition(BuildMouldDefinition()));

        var result = Workspace.UpdateMesh(updatedMesh);
        if (result.IsSuccess)
            Workspace = result.Value;
    }

    private MouldDefinition BuildMouldDefinition() => SelectedMouldType switch
    {
        MouldShapeType.Convex => new ConvexMouldDefinition(WallThickness, BaseHeight, BaseHeight) { AirChannels = Channels },
        MouldShapeType.Contoured => new ContouredMouldDefinition(WallThickness) { AirChannels = Channels },
        _ => new ConcaveMouldDefinition(WallThickness, BaseHeight, BaseHeight) { AirChannels = Channels }
    };

    private void UpdateMould()
    {
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
        var meshResult = Workspace.GetActiveMesh();
        if (meshResult.IsFailure)
            return TipLength;

        var statsResult = _engine.Evaluators.GetStatistics(meshResult.Value);
        if (statsResult.IsFailure)
            return TipLength;

        var topOffset = SelectedMouldType == MouldShapeType.Contoured ? WallThickness : BaseHeight;
        var mouldTopZ = statsResult.Value.MaxZ + topOffset;
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

    private void OnChannelPlaced(Vector3 point, Vector3 normal)
    {
        var totalLength = ComputeTotalLength(point.Z);

        IAirChannel domainModel = ChannelType switch
        {
            AirChannelType.Angled => new AngledAirChannel(point, normal, TipLength, totalLength, TipDiameter, ChannelDiameter / 2f, TipDepth),
            AirChannelType.Painted => new PaintedAirChannel([point], ChannelDiameter / 2f, totalLength, TipDepth),
            _ => new StraightAirChannel(point, TipLength, totalLength, TipDiameter, ChannelDiameter, TipDepth)
        };

        var channel = new AirChannelModel(Guid.NewGuid(), ChannelType, TipDiameter, ChannelDiameter, TipLength, domainModel);

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
            _sceneManager.UpdateMesh(meshResult.Value);
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
        {
            var updateResult = _sceneManager.UpdateMesh(meshResult.Value);
            if (updateResult.IsFailure)
                _alert.ShowError(updateResult.Error.Description);
        }

        _sceneManager.UpdateChannels(Channels);
        if (SelectedChannelId != Guid.Empty)
            _sceneManager.SelectChannel(SelectedChannelId);
        UpdateMould();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }
}
