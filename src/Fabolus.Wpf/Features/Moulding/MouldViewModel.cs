using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Viewport;
using System.Collections.ObjectModel;
using System.Numerics;
using static MR.Features.Const_MeasureResult;

namespace Fabolus.Wpf.Features.Moulding;

public partial class MouldViewModel : ObservableObject, IViewState
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly MouldSceneManager _sceneManager;

    private Workspace Workspace { get; set; }

    public ObservableCollection<IAirChannel> Channels { get; set; } = [];
    private IAirChannel PreviewChannel { get; set; }
    [ObservableProperty] private IAirChannel? _selectedchannel;

    private AirChannelType _channelType { get; set; } = AirChannelType.Straight;

    [ObservableProperty] private float _tipDiameter;
    [ObservableProperty] private float _tipLength;
    [ObservableProperty] private float _tipDepth;
    [ObservableProperty] private float _channelDiameter;

    public bool IsStraightType => _channelType == AirChannelType.Straight;
    public bool IsAngledType => _channelType == AirChannelType.Angled;
    public bool IsPathType => _channelType == AirChannelType.Painted;

    private MouldDefinition MouldDefinition { get; set; }

    public ISceneManager SceneManager => _sceneManager;

    public MouldViewModel() : this(WeakReferenceMessenger.Default, new AlertDialog(), new GeometryMeshLib.GeometryEngine(new FileSystem())) { }
    public MouldViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;

        _sceneManager = new MouldSceneManager(_engine);
    }

    public void Activate(Workspace workspace)
    {
        Workspace = workspace;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure)
            return;

        IMesh mesh = activeMeshResult.Value;
        var channelsResult = mesh.Metadata.AirChannels();

        Channels.Clear();
        if (channelsResult.HasValue) {
            foreach(var channel in channelsResult.Value)
                Channels.Add(channel);
        }

        PreviewChannel = new StraightAirChannel(Vector3.Zero, 3.0f, 15.0f, 3.0f, 5.0f);
        UpdateChannels();
        _sceneManager.UpdatePreviewChannel(PreviewChannel);

        var mouldResult = mesh.Metadata.MouldDefinition();
        MouldDefinition = mouldResult.HasValue
            ? mouldResult.Value
            : new ConcaveMouldDefinition();

        UpdateMould();

        var result = _sceneManager.UpdateWorkspace(Workspace);
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);
    }

    public Workspace Deactivate() => Workspace;

    private void UpdateChannels()
    {
        var result = _sceneManager.UpdateChannels(Channels);
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);
    }

    private void UpdateMould()
    {
        var result = _sceneManager.UpdateMould(MouldDefinition);
        if (result.IsFailure)
            _alert.ShowError(result.Error.Description);
    }

    [RelayCommand]
    public void SetChannelType(string channelType)
    {
        _channelType = channelType switch
        {
            "Straight" => AirChannelType.Straight,
            "Angled" => AirChannelType.Angled,
            "Path" => AirChannelType.Painted,
            _ => throw new Exception($"{channelType} doesn't match any AirChannelType")
        };
    }
}

