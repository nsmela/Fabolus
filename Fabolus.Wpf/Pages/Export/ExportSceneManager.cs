using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Export;

public class ExportSceneManager : SceneManager {
    private BolusModel _bolus;
    private MouldModel _mould;

    private Material _mouldSkin = DiffuseMaterials.Ruby;

    public ExportSceneManager() {
        SetMessaging();

        UpdateDisplay(_bolus);
    }

    protected override void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        _bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        _mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>();
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (BolusModel.IsNullOrEmpty(_bolus)) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        //bolus
        if (_mould.Geometry is null) { 
            models.Add(new DisplayModel3D {
                Geometry = _bolus.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = _skin
            });
        } else { 
        //mould
            models.Add(new DisplayModel3D {
                Geometry = _mould.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = _mouldSkin,
                IsTransparent = true
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }
}
