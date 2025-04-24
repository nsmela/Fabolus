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

namespace Fabolus.Wpf.Pages.Mould;

public class MouldSceneModel : SceneManager {
    private BolusModel _bolus;
    private MouldModel _mould;
    private Material _mouldSkin = DiffuseMaterials.Ruby;

    public MouldSceneModel() {
        SetMessaging();
    }

    protected override void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        //bolus
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, async (r, m) => await BolusUpdated(m.Bolus));
        WeakReferenceMessenger.Default.Register<MouldUpdatedMessage>(this, async (r, m) => await MouldUpdated(m.Mould));

        _bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
    }

    private async Task BolusUpdated(BolusModel bolus) {
        _bolus = bolus;
        UpdateDisplay(bolus);
    }

    private async Task MouldUpdated(MouldModel mould) {
        _mould = mould;
        UpdateDisplay(_bolus);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (_bolus is null || _bolus.Geometry is null || _bolus.Geometry.Positions is null || _bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        models.Add(new DisplayModel3D {
            Geometry = _bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = _skin
        });

        if (_mould is not null && _mould.Geometry.Indices.Count() > 0) {
            models.Add(new DisplayModel3D {
                Geometry = _mould.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = _mouldSkin
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }
}
