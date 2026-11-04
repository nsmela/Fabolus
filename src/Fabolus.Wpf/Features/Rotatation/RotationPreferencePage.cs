using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Rotatation;

/// <summary>Overhang shading thresholds and mesh opacity for the rotate view.</summary>
public sealed class RotationPreferencePage : IPreferencePage {
    /// <summary>Template key for the paired-threshold slider this page is built around.</summary>
    public const string OverhangRangeTemplate = "PreferenceRow.OverhangRange";

    public string Key => "rotation";
    public string Name => "Rotation";
    public string Description => "Starting values for the rotate view. Overhang angles are in degrees from the build plate.";
    public string Keywords => "rotate overhang angle threshold warning critical support opacity transparent see through";
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
        new NumberRow {
            Label = "Mesh opacity",
            Unit = "%",
            Caption = "How solid the mesh is drawn. Lower lets the bed and axes show through.",
            Minimum = RotationPreferences.Ranges.MeshOpacityMin,
            Maximum = RotationPreferences.Ranges.MeshOpacityMax,
            Interval = 5,
            StringFormat = "N0",
            Read = () => vm.Get<RotationPreferences>().MeshOpacity,
            Write = value => vm.Update<RotationPreferences>(settings => settings with { MeshOpacity = (float)value }),
        },
    ];
}
