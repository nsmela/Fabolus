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

    [ObservableProperty] private BaseSmoothingToolViewModel _setSmoothingViewModel = new MarchingCubesViewModel();
    [ObservableProperty] private double _minHeight;
    [ObservableProperty] private double _maxHeight;
    [ObservableProperty] private double _currentHeight;
    [ObservableProperty] private bool _showHeightSlider = false;

    partial void OnCurrentHeightChanged(double value) {

        //Send message to set contour at the current height
    }

    private BolusModel? _bolus;
    private bool _is_busy = false;

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
        BolusModel[] bolus = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        var smoothedBolus = await Task.Run(() => SetSmoothingViewModel.SmoothBolus(bolus[0]));

        WeakReferenceMessenger.Default.Send(new AddBolusMessage(smoothedBolus, BolusType.Smooth));
    }

    [RelayCommand] private void ClearSmoothed() => WeakReferenceMessenger.Default.Send(new ClearBolusMessage(BolusType.Smooth));

    #endregion

}
