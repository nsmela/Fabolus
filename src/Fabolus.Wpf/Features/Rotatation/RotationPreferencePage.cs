using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Rotatation;

/// <summary>Overhang shading thresholds for the rotate view.</summary>
public sealed class RotationPreferencePage : IPreferencePage {
    /// <summary>Template key for the paired-threshold slider this page is built around.</summary>
    public const string OverhangRangeTemplate = "PreferenceRow.OverhangRange";

    public string Key => "rotation";
    public string Name => "Rotation";
    public string Description => "Overhang shading in the rotate view, in degrees from the build plate.";
    public string Keywords => "rotate overhang angle threshold warning critical support";
    public string IconKey => "Icon.Preferences.Rotation";
    public int Order => 110;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        // The two angles are one control: a range slider whose thumbs cannot cross. No
        // descriptor expresses that, so this page supplies its own template.
        new CustomRow {
            Label = "Overhang thresholds",
            Caption = "Surfaces past the first threshold shade yellow, past the second red.",
            TemplateKey = OverhangRangeTemplate,
            Context = vm,
        },
    ];
}
