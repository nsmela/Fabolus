using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core;
using Fabolus.Core.BolusModel;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
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
    private AirChannelsCollection AirChannels { get; set; } = new();

    public MouldViewModel() {
        Bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        AirChannels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage());
    }

    [RelayCommand]
    private async Task GenerateMould() {
        if (Bolus is null) {
            throw new NullReferenceException("Bolus is null in MouldViewModel");
        }

        //var mesh = Bolus.TransformedMesh;
        //var tools = AirChannels.Values.Select(c => c.Geometry.ToDMesh()).ToArray();
        //var mould = new MouldModel(SimpleMouldGenerator.New().WithToolMeshes(tools).WithBolus(mesh));

        //WeakReferenceMessenger.Default.Send(new MouldUpdatedMessage(mould));
    }
}
