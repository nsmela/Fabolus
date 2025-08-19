using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using System.Windows.Input;
using HelixToolkit.Wpf.SharpDX;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;

using static Fabolus.Wpf.Bolus.BolusStore;
using Material = HelixToolkit.Wpf.SharpDX.Material;

namespace Fabolus.Wpf.Common.Scene;

public class SceneManager : IDisposable   {

    protected virtual Material _skin { get; } = PhongMaterials.Gray; 

    public SceneManager() {
        SetMessaging();
        SetInputBindings();

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

    protected virtual void SetMessaging() {
        //bolus
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateDisplay(m.Bolus));

        //mouse actions
        WeakReferenceMessenger.Default.Register<MeshMouseDownMessage>(this, (r, m) => OnMouseDown(m.Hits, m.OriginalEventArgs));
        WeakReferenceMessenger.Default.Register<MeshMouseMoveMessage>(this, (r, m) => OnMouseMove(m.Hits, m.OriginalEventArgs));
        WeakReferenceMessenger.Default.Register<MeshMouseUpMessage>(this, (r, m) => OnMouseUp(m.Hits, m.OriginalEventArgs));
    }

    protected virtual void SetDefaultInputBindings() => WeakReferenceMessenger.Default.Send(new MeshSetInputBindingsMessage(
        LeftMouseButton: new(),
        MiddleMouseButton: ViewportCommands.Pan,
        RightMouseButton: ViewportCommands.Rotate));

    protected virtual void SetInputBindings() =>
        WeakReferenceMessenger.Default.Send(new MeshDisplayInputsMessage(MeshDisplay.DefaultBindings));
    

    protected virtual void OnMouseDown(List<HitTestResult> hits, InputEventArgs args) {
    }

    protected virtual void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
    }

    protected virtual void OnMouseUp(List<HitTestResult> hits, InputEventArgs args) {
    }

    public virtual void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Send(new MeshDisplayInputsMessage(MeshDisplay.DefaultBindings));
    }
}

