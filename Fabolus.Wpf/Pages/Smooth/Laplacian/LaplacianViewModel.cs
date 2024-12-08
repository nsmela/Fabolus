using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Smooth.Laplacian;
public class LaplacianViewModel : BaseSmoothingToolViewModel {
    public override BolusModel SmoothBolus(BolusModel bolus) =>
        new BolusModel(LaplacianSmoothing.SmoothBolus(bolus));
    
}
