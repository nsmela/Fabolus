using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Input;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows.Input;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Channels.Straight;
using System;

using static Fabolus.Wpf.Bolus.BolusStore;
using Material = HelixToolkit.Wpf.SharpDX.Material;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManagerBase {

    private const double MAX_HEIGHT_OFFSET = 20.0;

    private BolusModel _bolus;
    private AirChannelsCollection _channels = [];
    private AirChannelSettings _settings;
    private MeshGeometry3D? _previewMesh;
    private IAirChannel _activeChannel;
    private Material _toolSkin = new DiffuseMaterial {
        Name = "Medium Purple",
        DiffuseColor = DiffuseMaterials.ToColor(0.574f, 0.4375f, 0.85546f)
    };
    private Material _channelSkin = DiffuseMaterials.Violet;
    private Material _selectedSkin = DiffuseMaterials.Turquoise;

    private Guid? BolusId => _bolus?.Geometry?.GUID;
    private float MaxHeight => (float)(_bolus.TransformedMesh().Height + MAX_HEIGHT_OFFSET);

    private MeshGeometry3D _airPockets;

    private IAirChannel? GetChannelByGeometryId(Guid id) {
        if (!_channels.Any(c => c.Value.Geometry.GUID == id)) {
            return null;
        }

        return _channels.FirstOrDefault(c => c.Value.Geometry.GUID == id).Value;
    }

    protected override void RegisterMessages() {
        //bolus
        _messenger.Register<BolusUpdatedMessage>(this, async (r, m) => await BolusUpdated(m.Bolus));

        //mouse actions
        _messenger.Register<MeshMouseDownMessage>(this, (r, m) => OnMouseDown(m.Hits, m.OriginalEventArgs));
        _messenger.Register<MeshMouseMoveMessage>(this, (r, m) => OnMouseMove(m.Hits, m.OriginalEventArgs));

        _messenger.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(m.Settings));
        _messenger.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        _messenger.Register<ActiveChannelUpdatedMessage>(this, async (r, m) => await ActiveAirChannelUpdated(m.Channel));

        _settings = _messenger.Send(new ChannelsSettingsRequestMessage()).Response;
        _activeChannel = _messenger.Send(new ActiveChannelRequestMessage()).Response;
        _channels = _messenger.Send(new AirChannelsRequestMessage()).Response;
 
        var bolus = _messenger.Send(new BolusRequestMessage()).Response;
        BolusUpdated(bolus);
    }

    protected override void RegisterInputBindings() {
        InputBindingCollection bindings = new InputBindingCollection { 
            // mouse controls
            new MouseBinding(ViewportCommands.Rotate, new MouseGesture(MouseAction.RightClick, ModifierKeys.None)),
            new MouseBinding(ViewportCommands.Pan, new MouseGesture(MouseAction.MiddleClick, ModifierKeys.None)),

            // key commands
            new KeyBinding(){ Command = ViewportCommands.BackView, Key = Key.B },
            new KeyBinding(){ Command = ViewportCommands.BottomView, Key = Key.D },
            new KeyBinding(){ Command = ViewportCommands.FrontView, Key = Key.F },
            new KeyBinding(){ Command = ViewportCommands.Reset, Key = Key.H },
            new KeyBinding(){ Command = ViewportCommands.LeftView, Key = Key.L },
            new KeyBinding(){ Command = ViewportCommands.RightView, Key = Key.R },
            new KeyBinding(){ Command = ViewportCommands.TopView, Key = Key.T },
            new KeyBinding(){ Command = new RelayCommand(DeleteChannel), Key = Key.Delete },
        };
        _messenger.Send(new MeshDisplayInputsMessage(bindings));
    }

    public ChannelsSceneManager() {
        RegisterMessages();
        RegisterInputBindings();
        SetAirPockets();
    }

    private void DeleteChannel() {
        if (_activeChannel is null) { return; }
        if (!_channels.ContainsKey(_activeChannel.GUID)) { return; }
        _channels.Remove(_activeChannel);

        _messenger.Send(new AirChannelsUpdatedMessage(_channels));

        var activeChannel = _settings[_activeChannel.ChannelType];
        _messenger.Send(new ActiveChannelUpdatedMessage(activeChannel));
    }

    private void SetAirPockets() {
        var bolus = _messenger.Send(new BolusRequestMessage()).Response;
        var results = AirPockets.Detect(bolus.TransformedMesh());
        var points = results.Select(r => new Vector3((float)r[0], (float)r[1], (float)r[2])); // convert from double [x, y, z] to Vector3

        MeshBuilder builder = new MeshBuilder();
        foreach (Vector3 point in points) {
            builder.AddSphere(point);
        }

        _airPockets = builder.ToMeshGeometry3D();

        if (_channels is null || _channels.Count > 0) { return; } // skip if channels already placed

        // check if preferences is set to allow autogenerating channels
        if(!_messenger.Send<PreferencesAutodetectChannelsRequest>().Response) { return; }

        var _default_air_channel = new StraightAirChannel();

        foreach (Vector3 point in points) {
            var channel = _default_air_channel.New();
            channel.Height = MaxHeight;
            HitTestResult hit = new HitTestResult() { PointHit = point };
            channel = channel.WithHit(hit);
            _channels.Add(channel);
        }

        _messenger.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task ActiveAirChannelUpdated(IAirChannel channel) {
        _activeChannel = channel;
        UpdateDisplay();
    }
    
    private async Task BolusUpdated(BolusModel? bolus) {
        _bolus = bolus;
        UpdateDisplay();
    }

    private async Task ChannelsUpdated(AirChannelsCollection channels) {
        _channels = channels;
        UpdateDisplay();
    }

    private async Task SettingsUpdated(AirChannelSettings settings) {
        _settings = settings;
        UpdateDisplay();
    }

    void OnMouseDown(List<HitTestResult> hits, InputEventArgs args) {
        //catch and ignored mouse buttons and exit
        var mouse = args as MouseButtonEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
            || mouse.MiddleButton == MouseButtonState.Pressed) {
            return;
        }

        //clear if nothing was hit
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) {
            //if nothing hit, then deselect the channel
            UpdateSelectedChannel(null);
            return;
        }

        //check if clicked on an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = GetChannelByGeometryId(id);

        if (channelHit is not null) {
            UpdateSelectedChannel(channelHit);
            args.Handled = true; // mark as handled to prevent further processing
            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        AddChannel(bolusHit); // will also update display via response from messaging
        args.Handled = true; // mark as handled to prevent further processing
    }

    void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) {
            UpdateDisplay();
            return; 
        }

        var mouse = args as MouseEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
            || mouse.MiddleButton == MouseButtonState.Pressed
            || mouse.LeftButton == MouseButtonState.Pressed) {
            UpdateDisplay();
            return;
        }

        //check if over an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = GetChannelByGeometryId(id);

        if (channelHit is not null) {
            UpdateDisplay();
            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        SetPreviewChannel(bolusHit);
    }

    void UpdateDisplay() {
        if (BolusModel.IsNullOrEmpty(_bolus)) {
            _messenger.Send(new MeshDisplayUpdatedMessage());
            return;
        }

        var models = new List<DisplayModel3D>();

        models.Add( new DisplayModel3D {
            Geometry = _bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Gray,
        });

        foreach(var channel in _channels.Values) {
            models.Add(new DisplayModel3D {
                Geometry = channel.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = channel.GUID == _activeChannel.GUID
                 ? _selectedSkin
                 : _channelSkin
            });
        }

        if (_previewMesh is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _previewMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = _toolSkin
            });
        }

        // shows air pockets
        if (_airPockets is not null) {

            models.Add(new DisplayModel3D {
                Geometry = _airPockets,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Orange
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

    private void UpdateSelectedChannel(IAirChannel? channel) {
        if (channel is null) {
            var newChannel = _settings[_activeChannel.ChannelType];
            _messenger.Send(new ActiveChannelUpdatedMessage(newChannel));
            return;
        }

        _settings[channel.ChannelType] = channel;
        _messenger.Send(new ChannelSettingsUpdatedMessage(_settings));
        _messenger.Send(new ActiveChannelUpdatedMessage(channel));
    }

    private void AddChannel(HitTestResult? hit) {
        if (hit is null) { return; }
        var channel = _settings[_activeChannel.ChannelType].New();
        channel.Height = MaxHeight;
        channel = channel.WithHit(hit);
        _channels.Add(channel);

        _messenger.Send(new AirChannelsUpdatedMessage(_channels));
        _messenger.Send(new ActiveChannelUpdatedMessage(channel));
    }

    private void SetPreviewChannel(HitTestResult? hit) {
        if (hit is null) { 
            _channels.PreviewChannel = null;
        }
        else {
            var channel = _settings[_activeChannel.ChannelType].New();
            channel.Height = MaxHeight;
            channel = channel.WithHit(hit, true);
            _channels.PreviewChannel = channel;
            _previewMesh = channel.Geometry;
        }

        UpdateDisplay();
    }
}
