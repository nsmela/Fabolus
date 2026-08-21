using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Configuration;

namespace Fabolus.Wpf.Features.AppPreferences;

// ============================================================
//  MESSAGES
// ============================================================
public sealed record AppPreferenceUpdateMessage(string Key, object Value);

public class AppPreferenceRequestMessage : RequestMessage<object> {
    public string Key { get; }
    public AppPreferenceRequestMessage(string key) { Key = key; }
}

// ============================================================
//  STORE
// ============================================================
public class AppPreferencesStore {
    private readonly Configuration _appConfig;
    private readonly UISettings _settings;

    public AppPreferencesStore() {
        _appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        if (_appConfig.Sections[UISettings.Label] is null) {
            _appConfig.Sections.Add(UISettings.Label, new UISettings {
                DefaultImportFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                DefaultExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DefaultExportFormat = ExportFormat.Stl.ToString(),
                PrintBedWidth = 250.0f,
                PrintBedDepth = 250.0f,
                ShowBedGrid = true,
                AutodetectChannels = true,
                ChannelDiameter = 4.0f,
                ViewportBackground = ViewportBackground.Graphite.ToString(),
                SplitViewEnabled = false,
                CutViewEnabled = false,
            });
        }

        _settings = _appConfig.GetSection(UISettings.Label) as UISettings
            ?? throw new ConfigurationErrorsException($"The preference section '{UISettings.Label}' is not properly configured.");

        RegisterMessages();
        RegisterRequests();
    }

    private void RegisterMessages() {
        WeakReferenceMessenger.Default.Register<AppPreferenceUpdateMessage>(this, (_, msg) => {
            // Convert enums to string if necessary, as the configuration properties for enums are typed as string
            object valueToSave = msg.Value is Enum e ? e.ToString() : msg.Value;
            
            _settings[msg.Key] = valueToSave;
            _appConfig.Save();
        });
    }

    private void RegisterRequests() {
        WeakReferenceMessenger.Default.Register<AppPreferencesStore, AppPreferenceRequestMessage>(this, (_, msg) => {
            msg.Reply(_settings[msg.Key]);
        });
    }
}
