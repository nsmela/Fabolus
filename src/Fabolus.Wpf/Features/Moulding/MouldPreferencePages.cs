using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Moulding;

/// <summary>
/// Air channel detection and defaults.
///
/// Stored on the print bed record, shown on its own page - which is the reason a section is a
/// separate thing from a settings record.
/// </summary>
public sealed class AirChannelPreferencePage : IPreferencePage {
    public string Key => "channels";
    public string Name => "Air Channels";
    public string Description => "Automatic channel detection and defaults.";
    public string Keywords => "autodetect diameter vent";
    public string IconKey => "Icon.Preferences.Channels";
    public int Order => 140;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new ToggleRow {
            Label = "Generate air channels automatically",
            Caption = "Detect and place channels when a mesh is imported.",
            Read = () => vm.Get<PrintBedPreferences>().AutodetectChannels,
            Write = value => vm.Update<PrintBedPreferences>(settings => settings with { AutodetectChannels = value }),
        },
        new NumberRow {
            Label = "Default channel diameter",
            Unit = "mm",
            Minimum = PrintBedPreferences.Ranges.ChannelDiameterMin,
            Maximum = PrintBedPreferences.Ranges.ChannelDiameterMax,
            Interval = 0.5,
            Read = () => vm.Get<PrintBedPreferences>().ChannelDiameter,
            Write = value => vm.Update<PrintBedPreferences>(settings => settings with { ChannelDiameter = (float)value }),
        },
    ];
}

/// <summary>Starting values for a newly generated mould, including its trough.</summary>
public sealed class MouldPreferencePage : IPreferencePage {
    public string Key => "mould";
    public string Name => "Mould";
    public string Description => "Starting values for a newly generated mould.";
    public string Keywords => "shape convex concave contoured wall thickness base height trough depth margin";
    public string IconKey => "Icon.Preferences.Mould";
    public int Order => 150;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new SegmentedRow {
            Label = "Shape",
            Choices = [
                new(MouldShapeType.Convex, "Convex"),
                new(MouldShapeType.Concave, "Concave"),
                new(MouldShapeType.Contoured, "Contoured"),
            ],
            Read = () => vm.Get<MouldPreferences>().Shape,
            Write = value => vm.Update<MouldPreferences>(settings => settings with { Shape = (MouldShapeType)value }),
        },
        new NumberRow {
            Label = "Wall thickness",
            Unit = "mm",
            Minimum = MouldPreferences.Ranges.WallThicknessMin,
            Maximum = MouldPreferences.Ranges.WallThicknessMax,
            Interval = 0.5,
            Read = () => vm.Get<MouldPreferences>().WallThickness,
            Write = value => vm.Update<MouldPreferences>(settings => settings with { WallThickness = (float)value }),
        },
        new NumberRow {
            Label = "Base height",
            Unit = "mm",
            Minimum = MouldPreferences.Ranges.BaseHeightMin,
            Maximum = MouldPreferences.Ranges.BaseHeightMax,
            StringFormat = "N0",
            Read = () => vm.Get<MouldPreferences>().BaseHeight,
            Write = value => vm.Update<MouldPreferences>(settings => settings with { BaseHeight = (float)value }),
        },

        // A contoured shell hugs the bolus, so it has no flat top to recess a basin into.
        new HeaderRow {
            Label = "TROUGH",
            Caption = "The basin excess silicone pools in while the mould fills.",
        }.EnabledWhen(() => vm.Get<MouldPreferences>().Shape != MouldShapeType.Contoured),
        new NumberRow {
            Label = "Depth",
            Unit = "mm",
            Caption = "0 leaves the top of the mould solid.",
            Minimum = MouldPreferences.Ranges.TroughHeightMin,
            Maximum = MouldPreferences.Ranges.TroughHeightMax,
            Interval = 0.5,
            Read = () => vm.Get<MouldPreferences>().TroughHeight,
            Write = value => vm.Update<MouldPreferences>(settings => settings with { TroughHeight = (float)value }),
        }.EnabledWhen(() => vm.Get<MouldPreferences>().Shape != MouldShapeType.Contoured),
        new SegmentedRow {
            Label = "Shape",
            Choices = [
                new(TroughShapeType.Footprint, "Footprint"),
                new(TroughShapeType.Channels, "Channels"),
            ],
            Read = () => vm.Get<MouldPreferences>().TroughShape,
            Write = value => vm.Update<MouldPreferences>(settings => settings with { TroughShape = (TroughShapeType)value }),
        }.EnabledWhen(() => vm.Get<MouldPreferences>().Shape != MouldShapeType.Contoured),
        new NumberRow {
            Label = "Margin",
            Unit = "mm",
            Caption = "How far the trough stops short of the mould wall.",
            Minimum = MouldPreferences.Ranges.TroughOffsetMin,
            Maximum = MouldPreferences.Ranges.TroughOffsetMax,
            Interval = 0.5,
            Read = () => vm.Get<MouldPreferences>().TroughOffset,
            Write = value => vm.Update<MouldPreferences>(settings => settings with { TroughOffset = (float)value }),
        }.EnabledWhen(() => vm.Get<MouldPreferences>().Shape != MouldShapeType.Contoured),
    ];
}
