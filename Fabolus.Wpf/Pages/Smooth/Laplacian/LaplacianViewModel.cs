using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth.Laplacian;

public partial class LaplacianViewModel : BaseSmoothingToolViewModel {
    public override BolusModel SmoothBolus(BolusModel bolus) =>
        new BolusModel(LaplacianSmoothing.SmoothBolus(bolus));


    [RelayCommand]
    private void ShowSmoothed() {
        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        var meshes = LaplacianSmoothing.SmoothSurfaces(bolus);
        WeakReferenceMessenger.Default.Send(new SmoothingModelsUpdatedMessage([meshes], []));
    } //=> WeakReferenceMessenger.Default.Send(new ClearBolusMessage(BolusType.Smooth));

}
