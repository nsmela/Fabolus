using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Smoothing;

/// <summary>Starting values for the smooth view.</summary>
public sealed class SmoothingPreferencePage : IPreferencePage {
    public string Key => "smoothing";
    public string Name => "Smoothing";
    public string Description =>
        "Starting values for the smooth view. A mesh that has already been smoothed reopens with the settings it was smoothed at.";
    public string Keywords =>
        "smooth intensity inflation iterations triangle ratio resolution voxel display heatmap cross section";
    public string IconKey => "Icon.Preferences.Smoothing";
    public int Order => 100;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new NumberRow {
            Label = "Intensity",
            Unit = "mm",
            Minimum = SmoothingPreferences.Ranges.IntensityMin,
            Maximum = SmoothingPreferences.Ranges.IntensityMax,
            Interval = 0.5,
            Read = () => vm.Get<SmoothingPreferences>().Intensity,
            Write = value => vm.Update<SmoothingPreferences>(settings => settings with { Intensity = (float)value }),
        },
        new NumberRow {
            Label = "Inflation",
            Unit = "mm",
            Caption = "Zero skips the inflation pass entirely.",
            Minimum = SmoothingPreferences.Ranges.InflationMin,
            Maximum = SmoothingPreferences.Ranges.InflationMax,
            Interval = 0.1,
            Read = () => vm.Get<SmoothingPreferences>().Inflation,
            Write = value => vm.Update<SmoothingPreferences>(settings => settings with { Inflation = (float)value }),
        },
        new NumberRow {
            Label = "Iterations",
            Minimum = SmoothingPreferences.Ranges.IterationsMin,
            Maximum = SmoothingPreferences.Ranges.IterationsMax,
            StringFormat = "N0",
            Read = () => vm.Get<SmoothingPreferences>().Iterations,
            Write = value => vm.Update<SmoothingPreferences>(settings => settings with { Iterations = (int)Math.Round(value) }),
        },
        new NumberRow {
            Label = "Triangle ratio",
            Caption = "Triangle budget after smoothing, relative to the original mesh.",
            Minimum = SmoothingPreferences.Ranges.RemeshRatioMin,
            Maximum = SmoothingPreferences.Ranges.RemeshRatioMax,
            Interval = 0.1,
            Read = () => vm.Get<SmoothingPreferences>().RemeshRatio,
            Write = value => vm.Update<SmoothingPreferences>(settings => settings with { RemeshRatio = (float)value }),
        },
        new NumberRow {
            Label = "Smoothness",
            Unit = "mm voxels",
            Caption = "Smaller voxels keep more detail and take longer to compute.",
            Minimum = SmoothingPreferences.Ranges.ResolutionMin,
            Maximum = SmoothingPreferences.Ranges.ResolutionMax,
            Interval = 0.25,
            Read = () => vm.Get<SmoothingPreferences>().Resolution,
            Write = value => vm.Update<SmoothingPreferences>(settings => settings with { Resolution = (float)value }),
        },
        new HeaderRow { Label = "DISPLAY" },
        new SegmentedRow {
            Label = "Opens in",
            Choices = [
                new(SmoothDisplayMode.None, "None"),
                new(SmoothDisplayMode.CrossSection, "Cross Section"),
                new(SmoothDisplayMode.Heatmap, "Heat Map"),
            ],
            Read = () => vm.Get<SmoothingPreferences>().DisplayMode,
            Write = value => vm.Update<SmoothingPreferences>(settings => settings with { DisplayMode = (SmoothDisplayMode)value }),
        },
    ];
}
