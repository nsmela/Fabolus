using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Smoothing;
using Fabolus.Wpf.Common.Bolus;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth.Poisson;

internal partial class PoissonViewModel : BaseSmoothingToolViewModel {

    [ObservableProperty] private int _depth;
    [ObservableProperty] private float _smoothScale;
    [ObservableProperty] private int _samplesPerNode;
    [ObservableProperty] private float _edgeLength;

    //list of smoothing settings to use with the slider
    //default values
    private static Dictionary<string, PoissonSettings> _defaultSmoothSettings = new Dictionary<string, PoissonSettings> {
        { "rough", new PoissonSettings { Depth = 9, Scale = 1.8f, SamplesPerNode = 2, EdgeLength = 1.0f } },
        { "standard", new PoissonSettings { Depth = 9, Scale = 1.8f, SamplesPerNode = 1, EdgeLength = 0.6f } },
        { "smooth", new PoissonSettings { Depth = 8, Scale = 1.4f, SamplesPerNode = 4, EdgeLength = 0.4f } },
    };

    private string SelectedSetting { get; set; } = "standard";
    private PoissonSmoothing PoissonSmoothing { get; init; } = new PoissonSmoothing();

    public PoissonViewModel() {
        SetSettings(SelectedSetting);
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
    }

    private PoissonSettings GetSettings() =>
        new PoissonSettings {
            Depth = this.Depth,
            SamplesPerNode = this.SamplesPerNode,
            Scale = this.SmoothScale,
            EdgeLength = this.EdgeLength
        };

    private void SetSettings(string label) {
        if (!_defaultSmoothSettings.ContainsKey(label)) { throw new Exception($"Label {label} is not found in default Poisson settings."); }

        var settings = _defaultSmoothSettings[label];
        Depth = settings.Depth;
        SmoothScale = settings.Scale;
        SamplesPerNode = settings.SamplesPerNode;
        EdgeLength = settings.EdgeLength;
    }

    public override BolusModel SmoothBolus(BolusModel bolus) {
        var settings = GetSettings();

        PoissonSmoothing.Initialize(bolus);
        var result = PoissonSmoothing.Smooth(settings);

        return new BolusModel(result);
    }
}
