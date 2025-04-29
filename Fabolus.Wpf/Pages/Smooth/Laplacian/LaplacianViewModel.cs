using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth.Laplacian;

public class LaplacianViewModel : BaseSmoothingToolViewModel {
    public override BolusModel SmoothBolus(BolusModel bolus) =>
        new BolusModel(LaplacianSmoothing.SmoothBolus(bolus));
    
}
