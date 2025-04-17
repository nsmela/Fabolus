using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Wpf.Common.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Pages.Mould;

public class MouldSceneModel : SceneManager {
    private BolusModel _bolus;
    private MeshGeometry3D _mould;

    public MouldSceneModel() {
        SetMessaging();
    }

    protected override void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        //bolus
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, async (r, m) => await BolusUpdated(m.Bolus));
    }

    private async Task BolusUpdated(BolusModel bolus) {
        _bolus = bolus;
    }
}
