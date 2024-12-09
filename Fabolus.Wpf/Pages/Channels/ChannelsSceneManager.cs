using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManager {

    public ChannelsSceneManager() : base() {

    }

    protected override void OnMouseDown(object? sender, Mouse3DEventArgs args) {
        base.OnMouseDown(sender, args);
    }

    protected override void OnMouseMove(object? sender, Mouse3DEventArgs args) {
        base.OnMouseMove(sender, args);
    }

    protected override void OnMouseUp(object? sender, Mouse3DEventArgs args) {
        base.OnMouseUp(sender, args);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        base.UpdateDisplay(bolus);
    }
}
