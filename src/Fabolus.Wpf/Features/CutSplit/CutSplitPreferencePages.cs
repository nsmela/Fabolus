using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.CutSplit;

/// <summary>The mesh cutting tool.</summary>
public sealed class CutPreferencePage : IPreferencePage {
    public string Key => "cut";
    public string Name => "Cut";
    public string Description => "The mesh cutting tool.";
    public string Keywords => "cut view toggle base mould scope";
    public string IconKey => "Icon.Preferences.Cut";
    public int Order => 120;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new ToggleRow {
            Label = "Cut view",
            Caption = "Adds the mesh cutting tool to the workflow.",
            Read = () => vm.EnableCutView,
            Write = value => vm.EnableCutView = value,
        },
        new DropdownRow {
            Label = "Show on",
            Caption = "Which meshes offer the cut tab.",
            Choices = [.. Enum.GetValues<CutViewScope>().Select(v => new PreferenceChoice(v, v.ToLabel()))],
            Read = () => vm.CutScope,
            Write = value => vm.CutScope = (CutViewScope)value,
        }.EnabledWhen(() => vm.EnableCutView),
    ];
}

/// <summary>The parting-line splitting tool.</summary>
public sealed class SplitPreferencePage : IPreferencePage {
    public string Key => "split";
    public string Name => "Split";
    public string Description => "The parting-line splitting tool.";
    public string Keywords => "split parting line view toggle";
    public string IconKey => "Icon.Preferences.Split";
    public int Order => 130;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new ToggleRow {
            Label = "Split view (for moulds)",
            Caption = "Adds the parting-line splitting tool to the workflow.",
            Read = () => vm.EnableSplitView,
            Write = value => vm.EnableSplitView = value,
        },
    ];
}
