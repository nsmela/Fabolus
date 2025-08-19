using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Configuration;

namespace Fabolus.Wpf.Features.AppPreferences;

// messages
public sealed record PreferencesSetImportFolderMessage(string ImportFolder);
public sealed record PreferencesSetExportFolderMessage(string ExportFolder);
public sealed record PreferencesSetPrintbedWidthMessage(float Width);
public sealed record PreferencesSetPrintbedDepthMessage(float Depth);
public sealed record PreferencesSetAutodetectChannelsMessage(bool AutodetectChannels);
public sealed record PreferencesSetSplitViewMessage(bool SplitViewEnabled);

// requests
public class PreferencesImportFolderRequest : RequestMessage<string> { }
public class PreferencesExportFolderRequest : RequestMessage<string> { }
public class PreferencesPrintbedWidthRequest : RequestMessage<float> { }
public class PreferencesPrintbedDepthRequest : RequestMessage<float> { }
public class PreferencesAutodetectChannelsRequest : RequestMessage<bool> { }
public class PreferencesSplitViewRequest : RequestMessage<bool> { }

public class AppPreferencesStore {
    // App Preferences Configuration
    private Configuration _appConfig;
    private UISettings _settings;

    public AppPreferencesStore() {
        _appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        if (_appConfig.Sections[UISettings.Label] is null) {
            _appConfig.Sections.Add(UISettings.Label, new UISettings() {
                DefaultImportFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                DefaultExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                PrintBedWidth = 250.0f, // Default print bed width
                PrintBedDepth = 250.0f, // Default print bed depth
                AutodetectChannels = true, // Default autodetect channels setting
                SplitViewEnabled = false, // show the splitting view
            });
        }

        var settings = _appConfig.GetSection(UISettings.Label) as UISettings;
        if (settings is null) {
            throw new ConfigurationErrorsException($"The preference section '{UISettings.Label}' is not properly configured.");
        }

        _settings = settings;

        // messages
        WeakReferenceMessenger.Default.Register<PreferencesSetImportFolderMessage>(this, (r, m) => {
            _settings.DefaultImportFolder = m.ImportFolder;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferencesSetExportFolderMessage>(this, (r, m) => {
            _settings.DefaultExportFolder = m.ExportFolder;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferencesSetPrintbedWidthMessage>(this, (r, m) => {
            _settings.PrintBedWidth = m.Width;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferencesSetPrintbedDepthMessage>(this, (r, m) => {
            _settings.PrintBedDepth = m.Depth;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferencesSetAutodetectChannelsMessage>(this, (r, m) => {
            _settings.AutodetectChannels = m.AutodetectChannels;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferencesSetSplitViewMessage>(this, (r, m) => {
            _settings.SplitViewEnabled = m.SplitViewEnabled;
            _appConfig.Save();
        });

        // requests
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesImportFolderRequest>(this, (r, m) => m.Reply(_settings.DefaultImportFolder));
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesExportFolderRequest>(this, (r, m) => m.Reply(_settings.DefaultExportFolder));
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesPrintbedWidthRequest>(this, (r, m) => m.Reply(_settings.PrintBedWidth));
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesPrintbedDepthRequest>(this, (r, m) => m.Reply(_settings.PrintBedDepth));
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesAutodetectChannelsRequest>(this, (r, m) => m.Reply(_settings.AutodetectChannels));
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferencesSplitViewRequest>(this, (r, m) => m.Reply(_settings.SplitViewEnabled));
    }
}
