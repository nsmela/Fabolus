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

    [ObservableProperty] private float _minimumHeight;
    [ObservableProperty] private float _maximumHeight;
    [ObservableProperty] private float _contourHeight;

    partial void OnSmoothingViewIndexChanged(int value) {
        SetSmoothingViewModel = GetView(value);
        ClearSmoothed();
    }

    partial void OnContourHeightChanged(float value) {
        if (_is_busy) { return; }
        _is_busy = true;

        WeakReferenceMessenger.Default.Send(new SmoothingContourMessage(value));

        _is_busy = false;
    }

    private BolusModel? _bolus;
    private bool _is_busy = false;

    #endregion

    public SmoothingViewModel() {
        _bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());

        MinimumHeight = (int)_bolus.Geometry.Bound.Minimum.Z;
        MaximumHeight = (int)_bolus.Geometry.Bound.Maximum.Z;

        _is_busy = true;
        ContourHeight = 0.0f;
        _is_busy = false;
    }

    #region Commands

    [RelayCommand]
    public async Task Smooth() {
        if (_bolus is null || _bolus.Mesh.IsEmpty()) {
            ErrorMessage("Smoothing Error", "Unable to smooth an empty model");
            return; 
        }

        var smoothedBolus = await Task.Run(() => SetSmoothingViewModel.SmoothBolus(_bolus));

        WeakReferenceMessenger.Default.Send(new AddBolusMessage(smoothedBolus, BolusType.Smooth));
    }

    [RelayCommand] private void ClearSmoothed() => WeakReferenceMessenger.Default.Send(new ClearBolusMessage(BolusType.Smooth));

    #endregion

}
