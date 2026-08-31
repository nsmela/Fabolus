namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>Default folders, export format and viewport appearance.</summary>
public sealed class GeneralPreferenceSection : IPreferenceSection {
    public string Key => "general";
    public string Name => "General";
    public string Description => "Default folders, export format and viewport appearance.";

    // Carries the old Appearance page's terms too, so a search for "theme" or "background"
    // still finds the setting now that it lives here.
    public string Keywords => "folder import export format file theme viewport background appearance";
    public string IconKey => "Icon.Preferences.General";
    public int Order => 0;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new FolderRow {
            Label = "Default import folder",
            Read = () => vm.ImportFilepath,
            Browse = vm.SetImportFolderCommand,
        },
        new FolderRow {
            Label = "Default export folder",
            Read = () => vm.ExportFilepath,
            Browse = vm.SetExportFolderCommand,
        },
        new SegmentedRow {
            Label = "Default file format",
            Choices = [
                new(ExportFormat.Stl, ExportFormat.Stl.ToLabel()),
                new(ExportFormat.ThreeMF, ExportFormat.ThreeMF.ToLabel()),
            ],
            Read = () => vm.ExportFormat,
            Write = value => vm.ExportFormat = (ExportFormat)value,
        },

        // Appearance used to be a page of its own, trailing the feature pages. It held one
        // setting, and it is stored on this same section's record, so it reads as a group here
        // rather than a sidebar entry of its own.
        new HeaderRow { Label = "APPEARANCE" },
        new SegmentedRow {
            Label = "Viewport background",
            Choices = [
                new(ViewportBackground.Graphite, "Graphite"),
                new(ViewportBackground.LightSteel, "Light steel"),
            ],
            Read = () => vm.ViewportBackground,
            Write = value => vm.ViewportBackground = (ViewportBackground)value,
        },
    ];
}

/// <summary>Build volume dimensions and the viewport grid.</summary>
public sealed class PrintBedPreferenceSection : IPreferenceSection {
    public string Key => "bed";
    public string Name => "Print Bed";
    public string Description => "Build volume dimensions, in millimetres.";
    public string Keywords => "width depth height size volume grid";
    public string IconKey => "Icon.Preferences.PrintBed";
    public int Order => 10;

    public IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel vm) => [
        new NumberRow {
            Label = "Width",
            Unit = "X",
            UnitBrushKey = "Brush.Axis.X",
            Minimum = PrintBedPreferences.Ranges.PrintBedMin,
            Maximum = PrintBedPreferences.Ranges.PrintBedMax,
            Read = () => vm.PrintbedWidth,
            Write = value => vm.PrintbedWidth = (float)value,
        },
        new NumberRow {
            Label = "Depth",
            Unit = "Y",
            UnitBrushKey = "Brush.Axis.Y",
            Minimum = PrintBedPreferences.Ranges.PrintBedMin,
            Maximum = PrintBedPreferences.Ranges.PrintBedMax,
            Read = () => vm.PrintbedDepth,
            Write = value => vm.PrintbedDepth = (float)value,
        },
        new ToggleRow {
            Label = "Show bed grid in viewport",
            Read = () => vm.ShowBedGrid,
            Write = value => vm.ShowBedGrid = value,
        },
    ];
}
