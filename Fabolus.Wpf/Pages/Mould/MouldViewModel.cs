using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Mould;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Mould;
public partial class MouldViewModel : BaseViewModel {
    public override string TitleText => "mold";

    public override SceneManager GetSceneManager => new MouldSceneModel();

    private BolusModel? Bolus { get; set; }

    public MouldViewModel() {
        Bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
    }

    [RelayCommand]
    private async Task GenerateMould() {
        if (Bolus is null) {
            throw new NullReferenceException("Bolus is null in MouldViewModel");
        }

        var mesh = Bolus.TransformedMesh;
        var mould = new MouldModel(SimpleMouldGenerator.New().WithBolus(mesh));

        WeakReferenceMessenger.Default.Send(new MouldUpdatedMessage(mould));
    }
}
