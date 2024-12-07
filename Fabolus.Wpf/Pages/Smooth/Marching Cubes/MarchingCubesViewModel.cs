using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;

namespace Fabolus.Wpf.Pages.Smooth.Marching_Cubes;

public partial class MarchingCubesViewModel : BaseSmoothingToolViewModel {

    #region Properties and Fields

    [ObservableProperty] private float _edgeLength;
    [ObservableProperty] private float _smoothSpeed;
    [ObservableProperty] private int _cells;
    [ObservableProperty] private int _iterations;

    private static Dictionary<string, MarchingCubesSettings> _defaultSmoothSettings = new Dictionary<string, MarchingCubesSettings> {
        { "rough", new MarchingCubesSettings { EdgeLength = 0.2f, SmoothSpeed=0.2f, Iterations=1, Cells=32 } },
        { "standard", new MarchingCubesSettings { EdgeLength = 0.4f, SmoothSpeed=0.2f, Iterations=1, Cells=64 } },
        { "smooth", new MarchingCubesSettings { EdgeLength = 0.6f, SmoothSpeed=0.4f, Iterations=2, Cells=128 } },
    };

    private MarchingCubesSettings Settings => new MarchingCubesSettings {
        EdgeLength = this._edgeLength,
        Cells = this._cells,
        SmoothSpeed = this._smoothSpeed,
        Iterations = this._iterations
    };

    private void SetSettings(string label) {
        var settings = _defaultSmoothSettings[label];

        EdgeLength = settings.EdgeLength;
        SmoothSpeed = settings.SmoothSpeed;
        Cells = settings.Cells;
        Iterations = settings.Iterations;
    }

    #endregion

    public MarchingCubesViewModel() {
        SetSettings("standard");
    }

    public override BolusModel SmoothBolus(BolusModel bolus) => new BolusModel(MarchingCubesSmoothing.Smooth(bolus, Settings));
    
}
