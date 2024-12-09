using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using static Fabolus.Wpf.Bolus.BolusStore;
using HelixToolkit.Wpf.SharpDX;
using Material = HelixToolkit.Wpf.SharpDX.Material;

namespace Fabolus.Wpf.Common.Scene;

public class SceneManager : IDisposable   {

    protected virtual Material _skin { get; } = PhongMaterials.Gray; 

    public SceneManager() {
        SetMessaging();
        SetDefaultInputBindings();

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        UpdateDisplay(bolus);
    }

    protected virtual void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var display = new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = _skin
        };

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([display]));
    }

    protected void SetMessaging() {
        //bolus
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateDisplay(m.Bolus));

        //mouse actions
        WeakReferenceMessenger.Default.Register<MeshMouseDownMessage>(this, (r, m) => OnMouseDown(m.sender, m.args));
        WeakReferenceMessenger.Default.Register<MeshMouseMoveMessage>(this, (r, m) => OnMouseMove(m.sender, m.args));
        WeakReferenceMessenger.Default.Register<MeshMouseUpMessage>(this, (r, m) => OnMouseUp(m.sender, m.args));
    }

    protected void SetDefaultInputBindings() => WeakReferenceMessenger.Default.Send(new MeshSetInputBindingsMessage(
        LeftMouseButton: ViewportCommands.Pan,
        MiddleMouseButton: ViewportCommands.Zoom,
        RightMouseButton: ViewportCommands.Rotate));

    protected virtual void OnMouseDown(object? sender, Mouse3DEventArgs args) {
    }

    protected virtual void OnMouseMove(object? sender, Mouse3DEventArgs args) {
    }

    protected virtual void OnMouseUp(object? sender, Mouse3DEventArgs args) {
    }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

