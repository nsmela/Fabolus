using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Smooth.WeightedOffsets;

public partial class WeightedOffsetsViewModel : BaseSmoothingToolViewModel {
    [ObservableProperty] private float _inflateDistance;
    [ObservableProperty] private float _smoothnessAngle;
    [ObservableProperty] private float _weightValue;
    [ObservableProperty] private double _cellSize;

    private static Dictionary<string, WeightedOffsetSettings> _defaultSettings = new Dictionary<string, WeightedOffsetSettings> {
        { "standard", new() { InflateDistance = 0.5f, WeightValue = 2.0f, SmoothingAngleDegs = 10, CellSize = 1.0f } },
    };

    private WeightedOffsetSettings Settings => new WeightedOffsetSettings {
        InflateDistance = this._inflateDistance,
        WeightValue = this._weightValue,
        SmoothingAngleDegs = this._smoothnessAngle,
        CellSize = this._cellSize,
    };

    public WeightedOffsetsViewModel() {
        SetSettings("standard");
    }

    private void SetSettings(string label) {
        var settings = _defaultSettings[label];
        InflateDistance = settings.InflateDistance;
        WeightValue = settings.WeightValue;
        SmoothnessAngle = settings.SmoothingAngleDegs;
        CellSize = settings.CellSize;
    }

    public override BolusModel SmoothBolus(BolusModel bolus) => new BolusModel(WeightedOffsetSmoothing.Smooth(bolus, Settings));
}
