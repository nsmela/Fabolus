using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Smoothing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth;
public abstract class BaseSmoothingToolViewModel : ObservableObject {
    public abstract BolusModel SmoothBolus(BolusModel bolus);
}
