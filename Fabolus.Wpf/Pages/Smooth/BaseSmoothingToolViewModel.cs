using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth;
public abstract class BaseSmoothingToolViewModel : ObservableObject {
    public abstract BolusModel SmoothBolus(BolusModel bolus);
}
