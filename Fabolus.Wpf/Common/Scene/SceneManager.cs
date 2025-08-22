using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;

using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Common.Scene;

public class SceneManager : SceneManagerBase  {

    protected BolusModel? _bolus;
    protected DisplayModel3D _displayModel = new();

    public SceneManager() {
        RegisterMessages();
        SetInputBindings();

        _bolus = _messenger.Send(new BolusRequestMessage()).Response;
        UpdateDisplay();
    }

    protected virtual void UpdateDisplay() {
        if (BolusModel.IsNullOrEmpty(_bolus)) {
            _messenger.Send(new MeshDisplayUpdatedMessage()); // clears the display
            return;
        }

        if (!BolusModel.IsNullOrEmpty(_bolus)) {
            _displayModel = new DisplayModel3D {
                Geometry = _bolus!.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Gray,
            };
        }

        _messenger.Send(new MeshDisplayUpdatedMessage(_displayModel));
    }

    protected virtual void SetInputBindings() =>
        _messenger.Send(new MeshDisplayInputsMessage(MeshDisplay.DefaultBindings));

    protected override void RegisterMessages() {
        //bolus
        _messenger.Register<BolusUpdatedMessage>(this, (r, m) => {
            _bolus = m.Bolus;
            UpdateDisplay();
        });
    }
}

