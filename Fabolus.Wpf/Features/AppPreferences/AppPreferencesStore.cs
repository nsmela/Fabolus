using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Configuration;

namespace Fabolus.Wpf.Features.AppPreferences;

// messages
public sealed record PreferencesSetImportFolderMessage(string ImportFolder);
public sealed record PreferencesSetExportFolderMessage(string ExportFolder);

public class PreferencesImportFolderRequest : RequestMessage<string> { }
public class PreferencesExportFolderRequest : RequestMessage<string> { }

public class AppPreferencesStore {
    // App Preferences Configuration
    private Configuration _appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
    private UISettings _settings;

    public AppPreferencesStore() {
        if (_appConfig.Sections[UISettings.Label] is null) {
            _appConfig.Sections.Add(UISettings.Label, new UISettings() {
                DefaultImportFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                DefaultExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            });
        }

        _settings = _appConfig.GetSection(UISettings.Label) as UISettings;

        // messages
        WeakReferenceMessenger.Default.Register<PreferencesSetImportFolderMessage>(this, (r,m) => _settings.DefaultImportFolder = m.ImportFolder);
        WeakReferenceMessenger.Default.Register<PreferencesSetExportFolderMessage>(this, (r, m) => _settings.DefaultExportFolder = m.ExportFolder);

        // requests
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesImportFolderRequest>(this, (r, m) => m.Reply(_settings.DefaultImportFolder));
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesExportFolderRequest>(this, (r, m) => m.Reply(_settings.DefaultExportFolder));
    }
}
