using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth.Laplacian;

public partial class LaplacianViewModel : BaseSmoothingToolViewModel {
    public override BolusModel SmoothBolus(BolusModel bolus) =>
        new BolusModel(LaplacianSmoothing.Smooth(bolus.TransformedMesh()));

}
