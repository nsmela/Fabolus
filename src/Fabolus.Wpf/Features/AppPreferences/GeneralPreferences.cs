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
) : IPreferenceSettings
{
    public static readonly GeneralPreferences Default = new(
        ImportFolder: Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        ExportFolder: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ExportFormat: ExportFormat.Stl,
        ViewportBackground: ViewportBackground.Graphite
    );

    public GeneralPreferences Clamped() => new(
        Directory.Exists(ImportFolder) ? ImportFolder : Default.ImportFolder,
        Directory.Exists(ExportFolder) ? ExportFolder : Default.ExportFolder,
        Enum.IsDefined(ExportFormat) ? ExportFormat : Default.ExportFormat,
        Enum.IsDefined(ViewportBackground) ? ViewportBackground : Default.ViewportBackground
    );
}
