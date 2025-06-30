using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow;
using Fabolus.Wpf.Pages.Smooth.Laplacian;
using Fabolus.Wpf.Pages.Smooth.Marching_Cubes;
using Fabolus.Wpf.Pages.Smooth.Poisson;
using Fabolus.Wpf.Pages.Smooth.WeightedOffsets;
using System;
using System.IO;
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

    //View control box
    [ObservableProperty] private IEnumerable<ViewModes> _views = Enum.GetValues(typeof(ViewModes)).Cast<ViewModes>();
    [ObservableProperty] private ViewModes _view = ViewModes.None;

    partial void OnCurrentHeightChanged(double value) {
        //Send message to set contour at the current height
        WeakReferenceMessenger.Default.Send(new SmoothingContourMessage(value));
    }

    partial void OnViewChanged(ViewModes oldValue, ViewModes newValue) {
        WeakReferenceMessenger.Default.Send(new SmoothingViewModeMessage(newValue));
    }

    // smoothing type
    [ObservableProperty] private int _smoothingTypeIndex = 0;
    partial void OnSmoothingTypeIndexChanged(int value) {
        SetSmoothingViewModel = GetView(value);
    }

    private BaseSmoothingToolViewModel GetView(int index) => index switch {
        0 => new PoissonViewModel(),
        1 => new MarchingCubesViewModel(),
        2 => new LaplacianViewModel(),
        3 => new WeightedOffsetsViewModel(),
        _ => throw new IndexOutOfRangeException("Index out of range")
    };

    private void UpdateSlider() {
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        if (bolus is null || bolus.Mesh.IsEmpty() || bolus.BolusType != BolusType.Smooth) {
            ShowHeightSlider = false;
            return;
        }

        MinHeight = (int)bolus.Mesh.Mesh.CachedBounds.Min.z + 1;
        MaxHeight = (int)bolus.Mesh.Mesh.CachedBounds.Max.z - 1;
        CurrentHeight = (MinHeight + MaxHeight) / 2; //set to middle of the range

        ShowHeightSlider = true;

    }

    private void UpdateMeshText() {
        BolusModel[] boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length == 0) {
            WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage("No bolus loaded."));
            return;
        }
        var filepath = boli[0].Filepath.Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Unknown Filepath";
        var text = $"Filepath:\r\n   {filepath}\r\nVolume[Original]:\r\n   {boli[0].VolumeToText}";

        if (boli.Length > 1 && boli[1].BolusType == BolusType.Smooth) {
            text += $"\r\nVolume[Smoothed]:\r\n   {boli[1].VolumeToText}";
        }

        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage(text));
    }

    #endregion

    public SmoothingViewModel() {
        UpdateSlider();
        UpdateMeshText();
    }

    #region Commands

    [RelayCommand]
    public async Task Smooth() {
        BolusModel[] boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length == 0 || boli[0].Mesh.IsEmpty()) {
            ErrorMessage("Smoothing Error", "Unable to smooth an empty model");
            return; 
        }
        var smoothedBolus = await Task.Run(() => SetSmoothingViewModel.SmoothBolus(boli[0]));

        WeakReferenceMessenger.Default.Send(new AddBolusMessage(smoothedBolus, BolusType.Smooth));
        UpdateSlider();
        UpdateMeshText();
    }

    [RelayCommand]
    private void ClearSmoothed() {
        WeakReferenceMessenger.Default.Send(new ClearBolusMessage(BolusType.Smooth));
        UpdateSlider();
        UpdateMeshText();
    }

    #endregion

}
