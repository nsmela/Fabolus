using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.Smooth.Laplacian;
using Fabolus.Wpf.Pages.Smooth.Marching_Cubes;
using Fabolus.Wpf.Pages.Smooth.Poisson;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public partial class SmoothingViewModel : BaseViewModel {
    #region Inheritance

    public override string TitleText => "Smooth";
    public override SceneManager GetSceneManager => new SmoothSceneManager(); //for displaying the mesh

    #endregion

    #region Properties and Events

    private BaseSmoothingToolViewModel GetView(int index) => index switch {
        0 => new PoissonViewModel(),
        1 => new MarchingCubesViewModel(),
        2 => new LaplacianViewModel(),
        _ => throw new IndexOutOfRangeException("Index out of range")
    };

    [ObservableProperty] private BaseSmoothingToolViewModel _setSmoothingViewModel = new PoissonViewModel();
    [ObservableProperty] private int _smoothingViewIndex = 0;

    partial void OnSmoothingViewIndexChanged(int value) {
        SetSmoothingViewModel = GetView(value);
        ClearSmoothed();
    }

    private BolusModel? _bolus;

    #endregion

    public SmoothingViewModel() {
        _bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
    }

    #region Commands

    [RelayCommand]
    public async Task Smooth() {
        if (_bolus is null || _bolus.Mesh.IsEmpty()) {
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
