using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.Smooth.Poisson;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public partial class SmoothingViewModel : BaseViewModel {
    #region Inheritance

    public override string TitleText => "Smooth";
    public override SceneManager GetSceneManager => new SmoothSceneManager(); //for displaying the mesh

    #endregion

    #region Properties and their Events

    [ObservableProperty] private BaseSmoothingToolViewModel _setSmoothingViewModel = new PoissonViewModel();
    private BolusModel? _bolus;

    #endregion

    public SmoothingViewModel() {
        _bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
    }

    #region Commands

    [RelayCommand]
    public async Task Smooth() {
        if (_bolus is null || _bolus.Mesh.TriangleCount == 0) {
            ErrorMessage("Smoothing Error", "Unable to smooth an empty model");
            return; 
        }

        ClearSmoothed();//removes the old smoothed mesh

        var smoothedBolus = await Task.Run(() => SetSmoothingViewModel.SmoothBolus(_bolus));

        WeakReferenceMessenger.Default.Send(new AddBolusMessage(smoothedBolus, BolusType.Smooth));
    }

    [RelayCommand] private void ClearSmoothed() => WeakReferenceMessenger.Default.Send(new ClearBolusMessage(BolusType.Smooth));

    #endregion

}
