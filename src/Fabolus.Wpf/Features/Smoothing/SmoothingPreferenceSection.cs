using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Smoothing;

/// <summary>Starting values for the smooth view.</summary>
public sealed class SmoothingPreferenceSection : IPreferenceSection {
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
            Read = () => vm.SmoothIntensity,
            Write = value => vm.SmoothIntensity = (float)value,
        },
        new NumberRow {
            Label = "Inflation",
            Unit = "mm",
            Caption = "Zero skips the inflation pass entirely.",
            Minimum = SmoothingPreferences.Ranges.InflationMin,
            Maximum = SmoothingPreferences.Ranges.InflationMax,
            Interval = 0.1,
            Read = () => vm.SmoothInflation,
            Write = value => vm.SmoothInflation = (float)value,
        },
        new NumberRow {
            Label = "Iterations",
            Minimum = SmoothingPreferences.Ranges.IterationsMin,
            Maximum = SmoothingPreferences.Ranges.IterationsMax,
            StringFormat = "N0",
            Read = () => vm.SmoothIterations,
            Write = value => vm.SmoothIterations = (int)Math.Round(value),
        },
        new NumberRow {
            Label = "Triangle ratio",
            Caption = "Triangle budget after smoothing, relative to the original mesh.",
            Minimum = SmoothingPreferences.Ranges.RemeshRatioMin,
            Maximum = SmoothingPreferences.Ranges.RemeshRatioMax,
            Interval = 0.1,
            Read = () => vm.SmoothRemeshRatio,
            Write = value => vm.SmoothRemeshRatio = (float)value,
        },
        new NumberRow {
            Label = "Smoothness",
            Unit = "mm voxels",
            Caption = "Smaller voxels keep more detail and take longer to compute.",
            Minimum = SmoothingPreferences.Ranges.ResolutionMin,
            Maximum = SmoothingPreferences.Ranges.ResolutionMax,
            Interval = 0.25,
            Read = () => vm.SmoothResolution,
            Write = value => vm.SmoothResolution = (float)value,
        },
        new HeaderRow { Label = "DISPLAY" },
        new SegmentedRow {
            Label = "Opens in",
            Choices = [
                new(SmoothDisplayMode.None, "None"),
                new(SmoothDisplayMode.CrossSection, "Cross Section"),
                new(SmoothDisplayMode.Heatmap, "Heat Map"),
            ],
            Read = () => vm.SmoothDisplay,
            Write = value => vm.SmoothDisplay = (SmoothDisplayMode)value,
        },
    ];
}
