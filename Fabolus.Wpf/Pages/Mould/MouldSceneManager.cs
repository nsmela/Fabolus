using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Wpf.Common.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;

namespace Fabolus.Wpf.Pages.Mould;

public class MouldSceneManager : SceneManager {
    private BolusModel _bolus;
    private AirChannelsCollection _channels = [];
    private MouldModel? _mould;
    private Material _mouldSkin = DiffuseMaterials.Ruby;
    private Material _channelsSkin = DiffuseMaterials.Glass;

    private bool _showBolus = true;
    private bool _showMould = true;
    private bool _showChannels = true;

    public MouldSceneManager() {
        SetMessaging();
    }

    protected override void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        //listening
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, async (r, m) => await BolusUpdated(m.Bolus));
        WeakReferenceMessenger.Default.Register<MouldUpdatedMessage>(this, async (r, m) => await MouldUpdated(m.Mould));

        _bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        _channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage());
        _mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>();
    }

    private async Task BolusUpdated(BolusModel bolus) {
        _bolus = bolus;
        UpdateDisplay(bolus);
    }

    private async Task MouldUpdated(MouldModel mould) {
        _mould = mould;
        _showBolus = mould.IsPreview;
        _showChannels = mould.IsPreview;

        UpdateDisplay(_bolus);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (BolusModel.IsNullOrEmpty(_bolus)) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        //bolus
        if (_showBolus) {
            models.Add(new DisplayModel3D {
                Geometry = _bolus.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = _skin
            });
        }

        //mould
        if (_showMould && !MouldModel.IsNullOrEmpty(_mould)) {
            models.Add(new DisplayModel3D {
                Geometry = _mould.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = _mouldSkin,
                Cull = true
            });
        }

        //channels
        if (_showChannels) {
            foreach (var channel in _channels.Values) {
                models.Add(new DisplayModel3D {
                    Geometry = channel.Geometry,
                    Transform = MeshHelper.TransformEmpty,
                    Skin = _channelsSkin
                });
            }
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }
}
