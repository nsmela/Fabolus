using Fabolus.Core.Features.Decal;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Decal;

/// <summary>Text engraved into or embossed onto the mesh.</summary>
public sealed class DecalPreferencePage : IPreferencePage {
    public string Key => "decals";
    public string Name => "Decals";
    public string Description => "Text engraved into or embossed onto the mesh.";
    public string Keywords => "text emboss engrave font label filename volume anchor";
    public string IconKey => "Icon.Preferences.Decals";
    public int Order => 160;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) {
        // Everything below the master switch is meaningless while the tool is off.
        bool Enabled() => vm.Get<DecalPreferences>().Enabled;

        IReadOnlyList<PreferenceChoice> anchors =
            [.. Enum.GetValues<DecalAnchor>().Select(a => new PreferenceChoice(a, a.ToLabel()))];

        return [
            new ToggleRow {
                Label = "Decal tool",
                Caption = "Adds the text decal step to the workflow.",
                Read = () => vm.Get<DecalPreferences>().Enabled,
                Write = value => vm.Update<DecalPreferences>(settings => settings with { Enabled = value }),
            },

            new HeaderRow {
                Label = "AUTOMATIC PLACEMENT",
                Caption = "Decals added for you when the decal step is opened on a mesh that has none yet.",
            }.EnabledWhen(Enabled),
            new DropdownRow {
                Label = "Place on",
                Caption = "Which mesh the automatic decals are applied to.",
                PickerWidth = 176,
                Choices = [.. Enum.GetValues<DecalAutoPlaceScope>().Select(s => new PreferenceChoice(s, s.ToLabel()))],
                Read = () => vm.Get<DecalPreferences>().Scope,
                Write = value => vm.Update<DecalPreferences>(settings => settings with { Scope = (DecalAutoPlaceScope)value }),
            }.EnabledWhen(Enabled),
            new AnchoredToggleRow {
                Label = "File name",
                Caption = "Engraves the mesh's file name at this anchor.",
                Choices = anchors,
                ReadAnchor = () => vm.Get<DecalPreferences>().FilenameAnchor,
                WriteAnchor = value => vm.Update<DecalPreferences>(settings => settings with { FilenameAnchor = (DecalAnchor)value }),
                ReadEnabled = () => vm.Get<DecalPreferences>().AutoPlaceFilename,
                WriteEnabled = value => vm.Update<DecalPreferences>(settings => settings with { AutoPlaceFilename = value }),
            }.EnabledWhen(Enabled),
            new AnchoredToggleRow {
                Label = "Volume",
                Caption = "Engraves the base mesh volume in cc at this anchor.",
                Choices = anchors,
                ReadAnchor = () => vm.Get<DecalPreferences>().VolumeAnchor,
                WriteAnchor = value => vm.Update<DecalPreferences>(settings => settings with { VolumeAnchor = (DecalAnchor)value }),
                ReadEnabled = () => vm.Get<DecalPreferences>().AutoPlaceVolume,
                WriteEnabled = value => vm.Update<DecalPreferences>(settings => settings with { AutoPlaceVolume = value }),
            }.EnabledWhen(Enabled),

            new HeaderRow {
                Label = "NEW DECAL DEFAULTS",
                Caption = "Starting values for every decal added in the decal view.",
            }.EnabledWhen(Enabled),
            new SegmentedRow {
                Label = "Operation",
                Choices = [
                    new(EmbossOperation.Emboss, "Emboss"),
                    new(EmbossOperation.Engrave, "Engrave"),
                ],
                Read = () => vm.Get<DecalPreferences>().Operation,
                Write = value => vm.Update<DecalPreferences>(settings => settings with { Operation = (EmbossOperation)value }),
            }.EnabledWhen(Enabled),
            new SegmentedRow {
                Label = "Font",
                Choices = [
                    new(DecalFont.Sans, "Sans"),
                    new(DecalFont.Mono, "Mono"),
                    new(DecalFont.Bold, "Bold"),
                ],
                Read = () => vm.Get<DecalPreferences>().Font,
                Write = value => vm.Update<DecalPreferences>(settings => settings with { Font = (DecalFont)value }),
            }.EnabledWhen(Enabled),
            new NumberRow {
                Label = "Cap height",
                Unit = "mm",
                Minimum = DecalPreferences.Ranges.CapHeightMin,
                Maximum = DecalPreferences.Ranges.CapHeightMax,
                Interval = 0.5,
                Read = () => vm.Get<DecalPreferences>().CapHeight,
                Write = value => vm.Update<DecalPreferences>(settings => settings with { CapHeight = (float)value }),
            }.EnabledWhen(Enabled),
            new NumberRow {
                Label = "Depth",
                Unit = "mm",
                Minimum = DecalPreferences.Ranges.DepthMin,
                Maximum = DecalPreferences.Ranges.DepthMax,
                Interval = 0.1,
                Read = () => vm.Get<DecalPreferences>().Depth,
                Write = value => vm.Update<DecalPreferences>(settings => settings with { Depth = (float)value }),
            }.EnabledWhen(Enabled),
            new NoteRow {
                Caption = "Cap height is a starting point only — a decal snapped to an anchor is still "
                        + "scaled to fit the room available there.",
            }.EnabledWhen(Enabled),
        ];
    }
}
