using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth.Marching_Cubes;

public partial class MarchingCubesViewModel : BaseSmoothingToolViewModel {

    #region Properties and Fields

    [ObservableProperty] private float _deflateDistance;
    [ObservableProperty] private float _inflateDistance;
    [ObservableProperty] private int _iterations;
    [ObservableProperty] private int _cells;

    private static Dictionary<string, MarchingCubesSettings> _defaultSmoothSettings = new Dictionary<string, MarchingCubesSettings> {
        { "standard", new MarchingCubesSettings { DeflateDistance = 0.1f, InflateDistance = 0.2f, Iterations=2, Cells=32 } },
        { "smooth", new MarchingCubesSettings { DeflateDistance = 0.4f, InflateDistance = 0.5f, Iterations=2, Cells=128 } },
    };

    private MarchingCubesSettings Settings => new MarchingCubesSettings {
        DeflateDistance = this._deflateDistance,
        InflateDistance = this._inflateDistance,
        Iterations = this._iterations,
        Cells = this._cells,
    };

    private void SetSettings(string label) {
        var settings = _defaultSmoothSettings[label];

        DeflateDistance = settings.DeflateDistance;
        InflateDistance = settings.InflateDistance;
        Cells = settings.Cells;
        Iterations = settings.Iterations;
    }

    #endregion

    public MarchingCubesViewModel() {
        SetSettings("standard");
    }

    public override BolusModel SmoothBolus(BolusModel bolus) => new BolusModel(MarchingCubesSmoothing.Smooth(bolus, Settings));
    
}
