using System;
using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.AppPreferences;

public class AppPreferencesStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public AppPreferencesStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fabolus_store_{Guid.NewGuid():N}");
        _path = Path.Combine(_tempDir, "preferences.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) { Directory.Delete(_tempDir, recursive: true); } } catch { }
    }

    [Fact]
    public void Get_ReturnsDefaults_WhenNoFileExists()
    {
        var store = new AppPreferencesStore(new StrongReferenceMessenger(), _path);

        Assert.Equal(MouldPreferences.Default, store.Get<MouldPreferences>());
        Assert.Equal(PrintBedPreferences.Default, store.Get<PrintBedPreferences>());
    }

    [Fact]
    public void Set_PersistsAcrossStoreInstances()
    {
        var messenger = new StrongReferenceMessenger();
        var store = new AppPreferencesStore(messenger, _path);

        store.Set(new MouldPreferences(
            MouldShapeType.Convex, 3.5f, 8.0f, 2.0f, 3.0f, TroughShapeType.Channels));

        var reopened = new AppPreferencesStore(new StrongReferenceMessenger(), _path);
        var mould = reopened.Get<MouldPreferences>();

        Assert.Equal(MouldShapeType.Convex, mould.Shape);
        Assert.Equal(3.5f, mould.WallThickness);
        Assert.Equal(TroughShapeType.Channels, mould.TroughShape);
    }

    [Fact]
    public void Set_ClampsBeforeStoring()
    {
        var store = new AppPreferencesStore(new StrongReferenceMessenger(), _path);

        store.Set(new PrintBedPreferences(
            Width: 99999f, Depth: 250f, ShowGrid: true, AutodetectChannels: true, ChannelDiameter: 4f));

        Assert.Equal(PrintBedPreferences.Ranges.PrintBedMax, store.Get<PrintBedPreferences>().Width);
    }

    [Fact]
    public void RequestMessage_IsAnsweredWithTheStoredSection()
    {
        var messenger = new StrongReferenceMessenger();
        var store = new AppPreferencesStore(messenger, _path);

        store.Set(new CutSplitPreferences(true, CutViewScope.Mould, true));

        var answered = messenger.Send(new PreferenceSectionRequestMessage<CutSplitPreferences>()).Response;

        Assert.True(answered.CutViewEnabled);
        Assert.Equal(CutViewScope.Mould, answered.CutScope);
    }

    [Fact]
    public void UpdateMessage_IsStoredAndPersisted()
    {
        var messenger = new StrongReferenceMessenger();
        var store = new AppPreferencesStore(messenger, _path);

        messenger.Send(new PreferenceSectionUpdateMessage<CutSplitPreferences>(
            new CutSplitPreferences(true, CutViewScope.Both, false)));

        var reopened = new AppPreferencesStore(new StrongReferenceMessenger(), _path);

        Assert.Equal(CutViewScope.Both, reopened.Get<CutSplitPreferences>().CutScope);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaultsRatherThanThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_path, "{ this is not json");

        var store = new AppPreferencesStore(new StrongReferenceMessenger(), _path);

        Assert.Equal(MouldPreferences.Default, store.Get<MouldPreferences>());
    }

    [Fact]
    public void UnknownKeysInTheFileSurviveASave()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_path, "{\"a_setting_from_the_future\":\"keep me\"}");

        var store = new AppPreferencesStore(new StrongReferenceMessenger(), _path);
        store.Set(RotationPreferences.Default);

        Assert.Contains("a_setting_from_the_future", File.ReadAllText(_path));
    }

    [Fact]
    public void EditingAPreference_ReachesTheStoreAndTheFile()
    {
        // The whole live path in one go: a view-model property change routes to its section,
        // the section message reaches the store, and the store writes it out.
        var messenger = new StrongReferenceMessenger();
        var store = new AppPreferencesStore(messenger, _path);

        var viewModel = new Fabolus.Wpf.Features.AppPreferences.PreferencesViewModel(
            messenger, new Moq.Mock<Fabolus.Wpf.Common.IAlertDialog>().Object);

        viewModel.Update<MouldPreferences>(m => m with { WallThickness = 4.25f });

        Assert.Equal(4.25f, store.Get<MouldPreferences>().WallThickness);

        var reopened = new AppPreferencesStore(new StrongReferenceMessenger(), _path);
        Assert.Equal(4.25f, reopened.Get<MouldPreferences>().WallThickness);
    }

    [Fact]
    public void RestoringDefaults_WritesEverySectionBack()
    {
        var messenger = new StrongReferenceMessenger();
        var store = new AppPreferencesStore(messenger, _path);
        store.Set(new PrintBedPreferences(300f, 300f, false, false, 6f));

        var viewModel = new Fabolus.Wpf.Features.AppPreferences.PreferencesViewModel(
            messenger, new Moq.Mock<Fabolus.Wpf.Common.IAlertDialog>().Object);

        viewModel.RestoreDefaultsCommand.Execute(null);

        Assert.Equal(PrintBedPreferences.Default, store.Get<PrintBedPreferences>());
        Assert.Equal(MouldPreferences.Default, store.Get<MouldPreferences>());
    }
}

public class PreferenceBagTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void SetFromText_ReadsBooleans(string text, bool expected)
    {
        var bag = new PreferenceBag();
        bag.SetFromText("flag", text);

        Assert.Equal(expected, bag.GetBool("flag", "Flag", !expected));
    }

    [Fact]
    public void SetFromText_ReadsWholeNumbers()
    {
        var bag = new PreferenceBag();
        bag.SetFromText("count", "3");

        Assert.Equal(3, bag.GetInt("count", "Count", fallback: 1, min: 0, max: 10));
    }

    [Fact]
    public void SetFromText_ReadsDecimalsAsNumbers()
    {
        // The old exe config stored everything as text; 5.5 has to come back as a float, not
        // as a string that every float read then rejects.
        var bag = new PreferenceBag();
        bag.SetFromText("channel_diameter", "5.5");

        Assert.Equal(5.5f, bag.GetFloat("channel_diameter", "Channel diameter", 4.0f, 1.0f, 20.0f));
    }

    [Fact]
    public void SetFromText_ReadsWholeNumbersAsFloatsToo()
    {
        var bag = new PreferenceBag();
        bag.SetFromText("print_bed_width", "250");

        Assert.Equal(250f, bag.GetFloat("print_bed_width", "Print bed width", 1f, 50f, 1000f));
    }

    [Fact]
    public void SetFromText_KeepsAnythingElseAsText()
    {
        var bag = new PreferenceBag();
        bag.SetFromText("mould_shape", "Convex");

        Assert.Equal("Convex", bag.GetString("mould_shape", "Mould shape", string.Empty));
    }

    [Fact]
    public void LegacyExeConfig_MigratesARealOldConfigFile()
    {
        // Verbatim shape of the config a pre-JSON build left behind, configSections entry and
        // all. The declared UISettings type no longer exists, which is exactly why this is read
        // as XML rather than through ConfigurationManager.
        const string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
            <configSections>
                <section name="UISettings" type="Fabolus.Wpf.Features.AppPreferences.UISettings, Fabolus.Wpf, Version=0.9.3.0, Culture=neutral, PublicKeyToken=null" />
            </configSections>
            <UISettings default_import_folder="C:\Users\someone\Downloads"
                default_export_folder="C:\Users\someone\Downloads" print_bed_width="250"
                print_bed_depth="250" show_bed_grid="true" autodetect_channels="true"
                channel_diameter="5.5" viewport_background="Graphite" measurement_units="Millimeters"
                app_theme="Dark" split_view_enabled="true" cut_view_enabled="false" />
        </configuration>
        """;

        var bag = PreferenceBag.FromDefaults();
        LegacyExeConfig.CopyFromXml(xml, bag);

        var printBed = PrintBedPreferences.Read(bag);
        var cutSplit = CutSplitPreferences.Read(bag);

        Assert.Equal(5.5f, printBed.ChannelDiameter);
        Assert.Equal(250f, printBed.Width);
        Assert.True(printBed.ShowGrid);
        Assert.True(cutSplit.SplitViewEnabled);
        Assert.False(cutSplit.CutViewEnabled);

        // measurement_units and app_theme were dropped by an earlier build; carrying them
        // across is harmless and keeps them if they ever come back.
        Assert.True(bag.ContainsKey("app_theme"));
    }

    [Fact]
    public void LegacyExeConfig_IgnoresAConfigWithNoSection()
    {
        var bag = PreferenceBag.FromDefaults();
        LegacyExeConfig.CopyFromXml("<configuration></configuration>", bag);

        Assert.Equal(PrintBedPreferences.Default, PrintBedPreferences.Read(bag));
    }

    [Fact]
    public void LegacyExeConfig_IgnoresMalformedXml()
    {
        var bag = PreferenceBag.FromDefaults();
        LegacyExeConfig.CopyFromXml("<configuration", bag);

        Assert.Equal(PrintBedPreferences.Default, PrintBedPreferences.Read(bag));
    }

    [Fact]
    public void MigratedTextValues_ReadBackThroughTheirSections()
    {
        // Exactly what LegacyExeConfig hands over: attribute names and their text.
        var bag = PreferenceBag.FromDefaults();
        bag.SetFromText("channel_diameter", "5.5");
        bag.SetFromText("split_view_enabled", "true");
        bag.SetFromText("mould_shape", "Convex");
        bag.SetFromText("print_bed_width", "300");

        Assert.Equal(5.5f, PrintBedPreferences.Read(bag).ChannelDiameter);
        Assert.Equal(300f, PrintBedPreferences.Read(bag).Width);
        Assert.True(CutSplitPreferences.Read(bag).SplitViewEnabled);
        Assert.Equal(MouldShapeType.Convex, MouldPreferences.Read(bag).Shape);
    }
}
