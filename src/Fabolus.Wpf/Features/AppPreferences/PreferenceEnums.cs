namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>Default mesh export format written by the app.</summary>
public enum ExportFormat {
    Stl,
    Obj,
    ThreeMF
}

/// <summary>Background treatment of the 3D viewport.</summary>
public enum ViewportBackground {
    Graphite,
    LightSteel
}

/// <summary>Display units used across the UI.</summary>
public enum MeasurementUnit {
    Millimeters,
    Centimeters,
    Inches
}

public static class PreferenceEnumExtensions {
    // Short labels that match the segmented controls in the UI.
    public static string ToLabel(this ExportFormat value) => value switch {
        ExportFormat.Stl => "STL",
        ExportFormat.Obj => "OBJ",
        ExportFormat.ThreeMF => "3MF",
        _ => value.ToString()
    };

    public static string ToLabel(this MeasurementUnit value) => value switch {
        MeasurementUnit.Millimeters => "mm",
        MeasurementUnit.Centimeters => "cm",
        MeasurementUnit.Inches => "in",
        _ => value.ToString()
    };
}
