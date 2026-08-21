namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>Default mesh export format written by the app.</summary>
public enum ExportFormat {
    Stl,
    ThreeMF
}

/// <summary>Background treatment of the 3D viewport.</summary>
public enum ViewportBackground {
    Graphite,
    LightSteel
}

public static class PreferenceEnumExtensions {
    // Short labels that match the segmented controls in the UI.
    public static string ToLabel(this ExportFormat value) => value switch {
        ExportFormat.Stl => "STL",
        ExportFormat.ThreeMF => "3MF",
        _ => value.ToString()
    };
}
