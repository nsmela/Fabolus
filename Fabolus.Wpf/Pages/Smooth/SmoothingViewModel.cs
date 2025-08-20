using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Pages.MainWindow;
using System.IO;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public partial class SmoothingViewModel : BaseViewModel {

    public override string TitleText => "Smooth";

    // smoothing settings

    [ObservableProperty] private float _deflateDistance;
    [ObservableProperty] private float _inflateDistance;
    [ObservableProperty] private int _iterations;
    [ObservableProperty] private double _cellSize;

    private static Dictionary<string, MarchingCubesSettings> _defaultSmoothSettings = new Dictionary<string, MarchingCubesSettings> {
        { "standard", new MarchingCubesSettings { DeflateDistance = 1.0f, InflateDistance = 0.1f, Iterations=1, CellSize = 1.0f } },
    };

    private MarchingCubesSettings Settings => new MarchingCubesSettings {
        DeflateDistance = this._deflateDistance,
        InflateDistance = this._inflateDistance,
        Iterations = this._iterations,
        CellSize = this._cellSize,
    };

    private void SetSettings(string label) {
        var settings = _defaultSmoothSettings[label];

        DeflateDistance = settings.DeflateDistance;
        InflateDistance = settings.InflateDistance;
        CellSize = settings.CellSize;
        Iterations = settings.Iterations;
    }

    // contour slider settings 

    [ObservableProperty] private double _minHeight;
    [ObservableProperty] private double _maxHeight;
    [ObservableProperty] private double _currentHeight;
    [ObservableProperty] private bool _showHeightSlider = false;

    partial void OnCurrentHeightChanged(double value) {
        //Send message to set contour at the current height
        Messenger.Send(new SmoothingContourMessage(value));
    }

    private void UpdateSlider() {
        var bolus = Messenger.Send(new BolusRequestMessage()).Response;
        if (BolusModel.IsNullOrEmpty(bolus)) {
            ShowHeightSlider = false;
            return;
        }

        MinHeight = (int)bolus.Mesh.Mesh.CachedBounds.Min.z + 1;
        MaxHeight = (int)bolus.Mesh.Mesh.CachedBounds.Max.z - 1;
        CurrentHeight = (MinHeight + MaxHeight) / 2; //set to middle of the range

        ShowHeightSlider = true;

    }



    //View control box
    [ObservableProperty] private IEnumerable<ViewModes> _views = Enum.GetValues(typeof(ViewModes)).Cast<ViewModes>();
    [ObservableProperty] private ViewModes _view = ViewModes.None;
    partial void OnViewChanged(ViewModes oldValue, ViewModes newValue) {
        WeakReferenceMessenger.Default.Send(new SmoothingViewModeMessage(newValue));
    }

    private void UpdateMeshText() {
        BolusModel[] boli = Messenger.Send(new AllBolusRequestMessage()).Response;
        if (BolusModel.IsNullOrEmpty(boli)) {
            Messenger.Send(new MeshInfoSetMessage("No bolus loaded."));
            return;
        }

        var filepath = boli[0].Filepath.Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Unknown Filepath";
        var text = $"Filepath:\r\n   {filepath}\r\nVolume[Original]:\r\n   {boli[0].Mesh.VolumeString()}";

        if (boli.Length > 1 && boli[1].BolusType == BolusType.Smooth) {
            text += $"\r\nVolume[Smoothed]:\r\n   {boli[1].Mesh.VolumeString()}";
        }

        Messenger.Send(new MeshInfoSetMessage(text));
    }

    public SmoothingViewModel() : base(new SmoothSceneManager()) {
        UpdateSlider();
        UpdateMeshText();
        SetSettings("standard");
    }

    [RelayCommand]
    public async Task Smooth() {
        BolusModel[] boli = Messenger.Send(new AllBolusRequestMessage()).Response;
        if (BolusModel.IsNullOrEmpty(boli)) {
            ShowErrorMessage("Smoothing Error", "Unable to smooth an empty model");
            return;
        }

        var smoothedBolus = await Task.Run(() => new BolusModel(MarchingCubesSmoothing.Smooth(boli[0], Settings)));

        Messenger.Send(new AddBolusMessage(smoothedBolus, BolusType.Smooth));
        UpdateSlider();
        UpdateMeshText();
    }

    [RelayCommand]
    private void ClearSmoothed() {
        Messenger.Send(new ClearBolusMessage(BolusType.Smooth));
        UpdateSlider();
        UpdateMeshText();
    }

    protected override void RegisterMessages() {
        throw new NotImplementedException();
    }

}
