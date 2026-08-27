using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Configuration;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;

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
                DecalsEnabled = true,
                DecalAutoPlaceScope = DecalAutoPlaceScope.Mould.ToString(),
                DecalAutoPlaceFilename = true,
                DecalFilenameAnchor = DecalAnchor.Front.ToString(),
                DecalAutoPlaceVolume = true,
                DecalVolumeAnchor = DecalAnchor.Back.ToString(),
                DecalDefaultFont = DecalFont.Sans.ToString(),
                DecalDefaultCapHeight = 6.0f,
                DecalDefaultDepth = 0.8f,
                DecalDefaultOperation = EmbossOperation.Engrave.ToString(),
                SmoothIterations = 1,
                SmoothIntensity = 1.5f,
                SmoothInflation = 0.2f,
                SmoothRemeshRatio = 1.0f,
                SmoothResolution = 1.0f,
                SmoothDisplayMode = Wpf.Features.Smoothing.SmoothDisplayMode.None.ToString(),
                OverhangWarningAngle = 45.0f,
                OverhangCriticalAngle = 65.0f,
                CutViewScope = AppPreferences.CutViewScope.Base.ToString(),
                MouldShape = MouldShapeType.Concave.ToString(),
                MouldWallThickness = 2.0f,
                MouldBaseHeight = 5.0f,
                MouldTroughHeight = 0.0f,
                MouldTroughOffset = 2.5f,
                MouldTroughShape = TroughShapeType.Footprint.ToString(),
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
