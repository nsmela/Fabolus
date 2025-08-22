using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Core.Meshes;

namespace Fabolus.Wpf.Pages.Mould;

public class MouldSceneManager : SceneManagerBase {
    private BolusModel _bolus;
    private AirChannelsCollection _channels = new();
    private MouldModel? _mould;

    // display models
    private DisplayModel3D _bolusModel;
    private DisplayModel3D _mouldModel;
    private List<DisplayModel3D> _channelsModels;

    private bool _showBolus = true;
    private bool _showMould = true;
    private bool _showChannels = true;

    public MouldSceneManager() {
        RegisterMessages();
        RegisterInputBindings();

        _bolus = _messenger.Send(new BolusRequestMessage());
        _bolusModel = new DisplayModel3D {
            Geometry = _bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Gray,
        };

        _channels = _messenger.Send(new AirChannelsRequestMessage());
        _channelsModels = [];
        foreach(var channel in _channels.Values){
            _channelsModels.Add(new DisplayModel3D {
                Geometry = channel.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Glass,
                IsTransparent = true
            });
        }

        _mould = _messenger.Send<MouldRequestMessage>();
        Task.Run(() => MouldUpdated(_mould)); // to update mould and display async
    }

    protected override void RegisterMessages() {
        //listening
        _messenger.Register<MouldUpdatedMessage>(this, async (r, m) => await MouldUpdated(m.Mould));
    }

    private async Task MouldUpdated(MouldModel mould) {
        _mould = mould;
        _showBolus = mould.IsPreview;
        _showChannels = mould.IsPreview;

        if (!MeshModel.IsNullOrEmpty(_mould)) {
            _mouldModel = new DisplayModel3D {
                Geometry = _mould.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
                IsTransparent = true
            };
        }

        await UpdateDisplay();
    }

    private async Task UpdateDisplay() {
        if (BolusModel.IsNullOrEmpty(_bolus)) {
            _messenger.Send(new MeshDisplayUpdatedMessage());
            return;
        }

        List<DisplayModel3D> models = [];

        if (_showBolus) {
            models.Add(_bolusModel); 
        }

        if (_showChannels) { 
            models.AddRange(_channelsModels); 
        }
        
        if (_showMould && DisplayModel3D.IsValid(_mouldModel)) {
            models.Add(_mouldModel);
        }

        await Task.Run(() => _messenger.Send(new MeshDisplayUpdatedMessage(models)));
    }
}
