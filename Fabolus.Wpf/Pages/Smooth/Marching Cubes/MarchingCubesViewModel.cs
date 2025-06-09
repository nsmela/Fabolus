using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth.Marching_Cubes;

public partial class MarchingCubesViewModel : BaseSmoothingToolViewModel {

    [ObservableProperty] private float _deflateDistance;
    [ObservableProperty] private float _inflateDistance;
    [ObservableProperty] private int _iterations;
    [ObservableProperty] private double _cellSize;

    private static Dictionary<string, MarchingCubesSettings> _defaultSmoothSettings = new Dictionary<string, MarchingCubesSettings> {
        { "standard", new MarchingCubesSettings { DeflateDistance = 5.0f, InflateDistance = 0.1f, Iterations=1, CellSize = 1.0f } },
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

    public MarchingCubesViewModel() {
        SetSettings("standard");
    }

    public override BolusModel SmoothBolus(BolusModel bolus) => new BolusModel(MarchingCubesSmoothing.Smooth(bolus, Settings));
    
}
