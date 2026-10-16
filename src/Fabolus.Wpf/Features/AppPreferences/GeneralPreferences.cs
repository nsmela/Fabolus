using System.IO;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// General application preferences (folders, export format, appearance).
/// </summary>
public sealed record GeneralPreferences(
    string ImportFolder,
    string ExportFolder,
    ExportFormat ExportFormat,
    ViewportBackground ViewportBackground,
    AppTheme AppTheme = AppTheme.Dark
) : IPreferenceSettings<GeneralPreferences>
{
    public static GeneralPreferences Default { get; } = new(
        ImportFolder: Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        ExportFolder: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ExportFormat: ExportFormat.Stl,
        ViewportBackground: ViewportBackground.Graphite,
        AppTheme: AppTheme.Dark
    );

    public static class Keys
    {
        public const string ImportFolder = "default_import_folder";
        public const string ExportFolder = "default_export_folder";
        public const string ExportFormat = "default_export_format";
        public const string ViewportBackground = "viewport_background";
        public const string AppTheme = "app_theme";
    }

    /// <summary>
    /// The export folder 1.0.0 defaulted to: %LOCALAPPDATA%, a hidden folder of program data that
    /// nobody browses to for an export. The store writes every setting out once any one changes,
    /// so this old default sits in existing preference files as though it had been chosen.
    /// </summary>
    private static string LegacyDefaultExportFolder { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static GeneralPreferences Read(IPreferenceReader reader) => new(
        reader.GetFolder(Keys.ImportFolder, "Import folder", Default.ImportFolder),
        WithoutLegacyDefault(reader.GetFolder(Keys.ExportFolder, "Export folder", Default.ExportFolder)),
        reader.GetEnum(Keys.ExportFormat, "Export format", Default.ExportFormat),
        reader.GetEnum(Keys.ViewportBackground, "Viewport background", Default.ViewportBackground),
        reader.GetEnum(Keys.AppTheme, "Application theme", Default.AppTheme)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.ImportFolder, ImportFolder);
        writer.Set(Keys.ExportFolder, ExportFolder);
        writer.SetEnum(Keys.ExportFormat, ExportFormat);
        writer.SetEnum(Keys.ViewportBackground, ViewportBackground);
        writer.SetEnum(Keys.AppTheme, AppTheme);
    }

    /// <summary>
    /// A stored export folder that is the 1.0.0 default is that default rather than a choice, so it
    /// moves to the current one. Someone who picked %LOCALAPPDATA% on purpose would have to pick it
    /// again - unlikely, and harmless.
    /// </summary>
    private static string WithoutLegacyDefault(string folder) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(folder),
            Path.TrimEndingDirectorySeparator(LegacyDefaultExportFolder),
            StringComparison.OrdinalIgnoreCase)
            ? Default.ExportFolder
            : folder;

    public GeneralPreferences Clamped() => new(
        Directory.Exists(ImportFolder) ? ImportFolder : Default.ImportFolder,
        Directory.Exists(ExportFolder) ? ExportFolder : Default.ExportFolder,
        Enum.IsDefined(ExportFormat) ? ExportFormat : Default.ExportFormat,
        Enum.IsDefined(ViewportBackground) ? ViewportBackground : Default.ViewportBackground,
        Enum.IsDefined(AppTheme) ? AppTheme : Default.AppTheme
    );
}
