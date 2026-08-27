using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Configuration;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;

namespace Fabolus.Wpf.Features.AppPreferences;

// ============================================================
//  MESSAGES
// ============================================================
public sealed record AppPreferenceUpdateMessage(string Key, object Value);

public class AppPreferenceRequestMessage : RequestMessage<object> {
    public string Key { get; }
    public AppPreferenceRequestMessage(string key) { Key = key; }
}

public sealed class PreferenceSectionRequestMessage<T> : RequestMessage<T> where T : class, IPreferenceSettings { }

public sealed record PreferenceSectionUpdateMessage<T>(T Section) where T : class, IPreferenceSettings;

// ============================================================
//  STORE
// ============================================================
public class AppPreferencesStore {
    private readonly Configuration _appConfig;
    private readonly UISettings _settings;

    public AppPreferencesStore() {
        _appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        if (_appConfig.Sections[UISettings.Label] is null) {
            var general = GeneralPreferences.Default;
            var bed = PrintBedPreferences.Default;
            var smooth = SmoothingPreferences.Default;
            var rotate = RotationPreferences.Default;
            var decal = DecalPreferences.Default;
            var mould = MouldPreferences.Default;
            var cutSplit = CutSplitPreferences.Default;

            _appConfig.Sections.Add(UISettings.Label, new UISettings {
                DefaultImportFolder = general.ImportFolder,
                DefaultExportFolder = general.ExportFolder,
                DefaultExportFormat = general.ExportFormat.ToString(),
                PrintBedWidth = bed.Width,
                PrintBedDepth = bed.Depth,
                ShowBedGrid = bed.ShowGrid,
                AutodetectChannels = bed.AutodetectChannels,
                ChannelDiameter = bed.ChannelDiameter,
                ViewportBackground = general.ViewportBackground.ToString(),
                AppTheme = AppTheme.Dark.ToString(),
                SplitViewEnabled = cutSplit.SplitViewEnabled,
                CutViewEnabled = cutSplit.CutViewEnabled,
                CutViewScope = cutSplit.CutScope.ToString(),
                DecalsEnabled = decal.Enabled,
                DecalAutoPlaceScope = decal.Scope.ToString(),
                DecalAutoPlaceFilename = decal.AutoPlaceFilename,
                DecalFilenameAnchor = decal.FilenameAnchor.ToString(),
                DecalAutoPlaceVolume = decal.AutoPlaceVolume,
                DecalVolumeAnchor = decal.VolumeAnchor.ToString(),
                DecalDefaultFont = decal.Font.ToString(),
                DecalDefaultCapHeight = decal.CapHeight,
                DecalDefaultDepth = decal.Depth,
                DecalDefaultOperation = decal.Operation.ToString(),
                SmoothIterations = smooth.Iterations,
                SmoothIntensity = smooth.Intensity,
                SmoothInflation = smooth.Inflation,
                SmoothRemeshRatio = smooth.RemeshRatio,
                SmoothResolution = smooth.Resolution,
                SmoothDisplayMode = smooth.DisplayMode.ToString(),
                OverhangWarningAngle = rotate.OverhangWarningAngle,
                OverhangCriticalAngle = rotate.OverhangCriticalAngle,
                MouldShape = mould.Shape.ToString(),
                MouldWallThickness = mould.WallThickness,
                MouldBaseHeight = mould.BaseHeight,
                MouldTroughHeight = mould.TroughHeight,
                MouldTroughOffset = mould.TroughOffset,
                MouldTroughShape = mould.TroughShape.ToString(),
            });
        }

        _settings = _appConfig.GetSection(UISettings.Label) as UISettings
            ?? throw new ConfigurationErrorsException($"The preference section '{UISettings.Label}' is not properly configured.");

        RegisterMessages();
        RegisterRequests();
    }

    public SmoothingPreferences GetSmoothingPreferences() => new SmoothingPreferences(
        _settings.SmoothIterations,
        _settings.SmoothIntensity,
        _settings.SmoothInflation,
        _settings.SmoothRemeshRatio,
        _settings.SmoothResolution,
        Enum.TryParse<SmoothDisplayMode>(_settings.SmoothDisplayMode, true, out var dm) ? dm : SmoothingPreferences.Default.DisplayMode
    ).Clamped();

    public RotationPreferences GetRotationPreferences() => new RotationPreferences(
        _settings.OverhangWarningAngle,
        _settings.OverhangCriticalAngle
    ).Clamped();

    public DecalPreferences GetDecalPreferences() => new DecalPreferences(
        _settings.DecalsEnabled,
        Enum.TryParse<DecalAutoPlaceScope>(_settings.DecalAutoPlaceScope, true, out var ds) ? ds : DecalPreferences.Default.Scope,
        _settings.DecalAutoPlaceFilename,
        Enum.TryParse<DecalAnchor>(_settings.DecalFilenameAnchor, true, out var dfa) ? dfa : DecalPreferences.Default.FilenameAnchor,
        _settings.DecalAutoPlaceVolume,
        Enum.TryParse<DecalAnchor>(_settings.DecalVolumeAnchor, true, out var dva) ? dva : DecalPreferences.Default.VolumeAnchor,
        Enum.TryParse<DecalFont>(_settings.DecalDefaultFont, true, out var df) ? df : DecalPreferences.Default.Font,
        _settings.DecalDefaultCapHeight,
        _settings.DecalDefaultDepth,
        Enum.TryParse<EmbossOperation>(_settings.DecalDefaultOperation, true, out var dop) ? dop : DecalPreferences.Default.Operation
    ).Clamped();

    public MouldPreferences GetMouldPreferences() => new MouldPreferences(
        Enum.TryParse<MouldShapeType>(_settings.MouldShape, true, out var ms) ? ms : MouldPreferences.Default.Shape,
        _settings.MouldWallThickness,
        _settings.MouldBaseHeight,
        _settings.MouldTroughHeight,
        _settings.MouldTroughOffset,
        Enum.TryParse<TroughShapeType>(_settings.MouldTroughShape, true, out var ts) ? ts : MouldPreferences.Default.TroughShape
    ).Clamped();

    public CutSplitPreferences GetCutSplitPreferences() => new CutSplitPreferences(
        _settings.CutViewEnabled,
        Enum.TryParse<CutViewScope>(_settings.CutViewScope, true, out var cs) ? cs : CutSplitPreferences.Default.CutScope,
        _settings.SplitViewEnabled
    ).Clamped();

    public GeneralPreferences GetGeneralPreferences() => new GeneralPreferences(
        _settings.DefaultImportFolder,
        _settings.DefaultExportFolder,
        Enum.TryParse<ExportFormat>(_settings.DefaultExportFormat, true, out var ef) ? ef : GeneralPreferences.Default.ExportFormat,
        Enum.TryParse<ViewportBackground>(_settings.ViewportBackground, true, out var vb) ? vb : GeneralPreferences.Default.ViewportBackground
    ).Clamped();

    public PrintBedPreferences GetPrintBedPreferences() => new PrintBedPreferences(
        _settings.PrintBedWidth,
        _settings.PrintBedDepth,
        _settings.ShowBedGrid,
        _settings.AutodetectChannels,
        _settings.ChannelDiameter
    ).Clamped();

    private void RegisterMessages() {
        WeakReferenceMessenger.Default.Register<AppPreferenceUpdateMessage>(this, (_, msg) => {
            object valueToSave = msg.Value is Enum e ? e.ToString() : msg.Value;
            _settings[msg.Key] = valueToSave;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<SmoothingPreferences>>(this, (_, msg) => {
            var s = msg.Section;
            _settings.SmoothIterations = s.Iterations;
            _settings.SmoothIntensity = s.Intensity;
            _settings.SmoothInflation = s.Inflation;
            _settings.SmoothRemeshRatio = s.RemeshRatio;
            _settings.SmoothResolution = s.Resolution;
            _settings.SmoothDisplayMode = s.DisplayMode.ToString();
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<RotationPreferences>>(this, (_, msg) => {
            var r = msg.Section;
            _settings.OverhangWarningAngle = r.OverhangWarningAngle;
            _settings.OverhangCriticalAngle = r.OverhangCriticalAngle;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<DecalPreferences>>(this, (_, msg) => {
            var d = msg.Section;
            _settings.DecalsEnabled = d.Enabled;
            _settings.DecalAutoPlaceScope = d.Scope.ToString();
            _settings.DecalAutoPlaceFilename = d.AutoPlaceFilename;
            _settings.DecalFilenameAnchor = d.FilenameAnchor.ToString();
            _settings.DecalAutoPlaceVolume = d.AutoPlaceVolume;
            _settings.DecalVolumeAnchor = d.VolumeAnchor.ToString();
            _settings.DecalDefaultFont = d.Font.ToString();
            _settings.DecalDefaultCapHeight = d.CapHeight;
            _settings.DecalDefaultDepth = d.Depth;
            _settings.DecalDefaultOperation = d.Operation.ToString();
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<MouldPreferences>>(this, (_, msg) => {
            var m = msg.Section;
            _settings.MouldShape = m.Shape.ToString();
            _settings.MouldWallThickness = m.WallThickness;
            _settings.MouldBaseHeight = m.BaseHeight;
            _settings.MouldTroughHeight = m.TroughHeight;
            _settings.MouldTroughOffset = m.TroughOffset;
            _settings.MouldTroughShape = m.TroughShape.ToString();
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<CutSplitPreferences>>(this, (_, msg) => {
            var c = msg.Section;
            _settings.CutViewEnabled = c.CutViewEnabled;
            _settings.CutViewScope = c.CutScope.ToString();
            _settings.SplitViewEnabled = c.SplitViewEnabled;
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<GeneralPreferences>>(this, (_, msg) => {
            var g = msg.Section;
            _settings.DefaultImportFolder = g.ImportFolder;
            _settings.DefaultExportFolder = g.ExportFolder;
            _settings.DefaultExportFormat = g.ExportFormat.ToString();
            _settings.ViewportBackground = g.ViewportBackground.ToString();
            _appConfig.Save();
        });

        WeakReferenceMessenger.Default.Register<PreferenceSectionUpdateMessage<PrintBedPreferences>>(this, (_, msg) => {
            var b = msg.Section;
            _settings.PrintBedWidth = b.Width;
            _settings.PrintBedDepth = b.Depth;
            _settings.ShowBedGrid = b.ShowGrid;
            _settings.AutodetectChannels = b.AutodetectChannels;
            _settings.ChannelDiameter = b.ChannelDiameter;
            _appConfig.Save();
        });
    }

    private void RegisterRequests() {
        WeakReferenceMessenger.Default.Register<AppPreferenceRequestMessage>(this, (_, msg) => {
            if (_settings[msg.Key] is object value) {
                msg.Reply(value);
            }
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<SmoothingPreferences>>(this, (_, msg) => {
            msg.Reply(GetSmoothingPreferences());
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<RotationPreferences>>(this, (_, msg) => {
            msg.Reply(GetRotationPreferences());
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<DecalPreferences>>(this, (_, msg) => {
            msg.Reply(GetDecalPreferences());
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<MouldPreferences>>(this, (_, msg) => {
            msg.Reply(GetMouldPreferences());
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<CutSplitPreferences>>(this, (_, msg) => {
            msg.Reply(GetCutSplitPreferences());
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<GeneralPreferences>>(this, (_, msg) => {
            msg.Reply(GetGeneralPreferences());
        });

        WeakReferenceMessenger.Default.Register<AppPreferencesStore, PreferenceSectionRequestMessage<PrintBedPreferences>>(this, (_, msg) => {
            msg.Reply(GetPrintBedPreferences());
        });
    }

    public object GetPreference(string key) => _settings[key];
}
