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

/// <summary>
/// A named anchor a decal can be auto-placed on. The names mirror the presets produced by
/// BasePresetPointsCalculator (Top / Front / Back) and MouldPresetPointsCalculator
/// (Front / Back / Left / Right / Curve 1 / Curve 2), so a value here resolves by name.
/// Not every anchor exists on every mesh - a decal falls back to the first available preset.
/// </summary>
public enum DecalAnchor {
    Top,
    Front,
    Back,
    Left,
    Right,
    Curve1,
    Curve2
}

/// <summary>Which meshes the automatic file name / volume decals are placed on.</summary>
public enum DecalAutoPlaceScope {
    /// <summary>Mould only. Nothing is auto-placed on a mesh without a mould.</summary>
    Mould,
    /// <summary>Base mesh only, whether or not a mould exists.</summary>
    Base,
    /// <summary>Both the mould and the base mesh underneath it.</summary>
    MouldAndBase,
    /// <summary>The mould when there is one, otherwise the base mesh.</summary>
    BaseIfNoMould
}

/// <summary>Which meshes the cut view is offered on.</summary>
public enum CutViewScope {
    /// <summary>Non-mould meshes only. There is nothing useful to cut out of a generated mould.</summary>
    Base,
    /// <summary>Generated moulds only.</summary>
    Mould,
    /// <summary>Both.</summary>
    Both
}

/// <summary>Application theme for UI controls.</summary>
public enum AppTheme {
    Dark,
    Light
}

public static class PreferenceEnumExtensions {
    // Short labels that match the segmented controls in the UI.
    public static string ToLabel(this ExportFormat value) => value switch {
        ExportFormat.Stl => "STL",
        ExportFormat.ThreeMF => "3MF",
        _ => value.ToString()
    };

    /// <summary>
    /// The preset name this anchor resolves against. Must stay in step with the string literals
    /// in BasePresetPointsCalculator and MouldPresetPointsCalculator.
    /// </summary>
    public static string ToPresetName(this DecalAnchor value) => value switch {
        DecalAnchor.Curve1 => "Curve 1",
        DecalAnchor.Curve2 => "Curve 2",
        _ => value.ToString()
    };

    public static string ToLabel(this DecalAnchor value) => value.ToPresetName();

    public static string ToLabel(this CutViewScope value) => value switch {
        CutViewScope.Base => "Base meshes",
        CutViewScope.Mould => "Moulds",
        CutViewScope.Both => "Both",
        _ => value.ToString()
    };

    public static string ToLabel(this DecalAutoPlaceScope value) => value switch {
        DecalAutoPlaceScope.Mould => "Mould only",
        DecalAutoPlaceScope.Base => "Base mesh only",
        DecalAutoPlaceScope.MouldAndBase => "Mould and base",
        DecalAutoPlaceScope.BaseIfNoMould => "Base only when no mould",
        _ => value.ToString()
    };

    public static string ToLabel(this AppTheme value) => value switch {
        AppTheme.Dark => "Dark",
        AppTheme.Light => "Light",
        _ => value.ToString()
    };
}
