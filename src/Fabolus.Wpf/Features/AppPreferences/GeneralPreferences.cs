using System.IO;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// General application preferences (folders, export format, appearance).
/// </summary>
public sealed record GeneralPreferences(
    string ImportFolder,
    string ExportFolder,
    ExportFormat ExportFormat,
    ViewportBackground ViewportBackground
) : IPreferenceSettings<GeneralPreferences>
{
    public static string SectionKey => "general";

    public static GeneralPreferences Default { get; } = new(
        ImportFolder: Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        ExportFolder: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ExportFormat: ExportFormat.Stl,
        ViewportBackground: ViewportBackground.Graphite
    );

    public static class Keys
    {
        public const string ImportFolder = "default_import_folder";
        public const string ExportFolder = "default_export_folder";
        public const string ExportFormat = "default_export_format";
        public const string ViewportBackground = "viewport_background";
    }

    public static GeneralPreferences Read(IPreferenceReader reader) => new(
        reader.GetFolder(Keys.ImportFolder, "Import folder", Default.ImportFolder),
        reader.GetFolder(Keys.ExportFolder, "Export folder", Default.ExportFolder),
        reader.GetEnum(Keys.ExportFormat, "Export format", Default.ExportFormat),
        reader.GetEnum(Keys.ViewportBackground, "Viewport background", Default.ViewportBackground)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.ImportFolder, ImportFolder);
        writer.Set(Keys.ExportFolder, ExportFolder);
        writer.SetEnum(Keys.ExportFormat, ExportFormat);
        writer.SetEnum(Keys.ViewportBackground, ViewportBackground);
    }

    public GeneralPreferences Clamped() => new(
        Directory.Exists(ImportFolder) ? ImportFolder : Default.ImportFolder,
        Directory.Exists(ExportFolder) ? ExportFolder : Default.ExportFolder,
        Enum.IsDefined(ExportFormat) ? ExportFormat : Default.ExportFormat,
        Enum.IsDefined(ViewportBackground) ? ViewportBackground : Default.ViewportBackground
    );
}
