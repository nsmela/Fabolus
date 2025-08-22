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

public class ExportSceneManager : SceneManagerBase {
    private BolusModel _bolus;
    private MouldModel _mould;
    private bool _showBolus;
    private bool _showMould;

    public ExportSceneManager() {
        RegisterMessages();
        RegisterInputBindings();

        UpdateDisplay();
    }

    protected override void RegisterMessages() { 
        _messenger.Register<ExportSceneManager, ExportShowBolusMessage>(this, (r,m) => {
            _showBolus = m.ShowBolus;
            UpdateDisplay();
        });

        _messenger.Register<ExportSceneManager, ExportShowMouldMessage>(this, (r, m) => {
            _showMould = m.ShowMould;
            UpdateDisplay();
        });
    }

    void UpdateDisplay() {
        _bolus = _messenger.Send<BolusRequestMessage>();
        _mould = _messenger.Send<MouldRequestMessage>();

        if (BolusModel.IsNullOrEmpty(_bolus)) {
            _messenger.Send(new MeshDisplayUpdatedMessage());
            return;
        }

        var models = new List<DisplayModel3D>();

        // show both models unless hovering over a button to export
        // then show the model about to be exported

        //bolus
        if (!_showMould ) { 
            models.Add(new DisplayModel3D {
                Geometry = _bolus.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Gray,
            });

        } 
        //mould
        if (!MouldModel.IsNullOrEmpty(_mould) && !_showBolus) { 
            models.Add(new DisplayModel3D {
                Geometry = _mould.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby,
                IsTransparent = true
            });
        }

        _messenger.Send(new MeshDisplayUpdatedMessage(models));
    }
}
