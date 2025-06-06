using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth.Marching_Cubes;

public partial class MarchingCubesViewModel : BaseSmoothingToolViewModel {

    #region Properties and Fields

    [ObservableProperty] private float _deflateDistance;
    [ObservableProperty] private float _inflateDistance;
    [ObservableProperty] private int _iterations;
    [ObservableProperty] private double _cellSize;

    private static Dictionary<string, MarchingCubesSettings> _defaultSmoothSettings = new Dictionary<string, MarchingCubesSettings> {
        { "standard", new MarchingCubesSettings { DeflateDistance = 0.1f, InflateDistance = 0.2f, Iterations=2, CellSize = 2.5f } },
        { "smooth", new MarchingCubesSettings { DeflateDistance = 0.4f, InflateDistance = 0.5f, Iterations=2, CellSize = 1.5f } },
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

    #endregion

    public MarchingCubesViewModel() {
        SetSettings("standard");
    }

    public override BolusModel SmoothBolus(BolusModel bolus) => new BolusModel(MarchingCubesSmoothing.Smooth(bolus, Settings));
    
}
