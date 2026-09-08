using System;
using System.IO;
using System.Linq;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.AppPreferences;

public class PreferenceProfileTests : IDisposable
{
    private readonly string _tempFile;

    public PreferenceProfileTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"fabolus_pref_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }

    private static PreferenceBag BagOf(params Action<PreferenceBag>[] writes)
    {
        var bag = PreferenceBag.FromDefaults();
        foreach (var write in writes) { write(bag); }
        return bag;
    }

    [Fact]
    public void WriteAndRead_RoundtripsSuccessfully()
    {
        var bag = BagOf(
            b => new GeneralPreferences(
                Path.GetTempPath(), Path.GetTempPath(), ExportFormat.ThreeMF, ViewportBackground.Graphite).Write(b),
            b => new PrintBedPreferences(300.0f, 300.0f, false, false, 6.0f).Write(b),
            b => new CutSplitPreferences(true, CutViewScope.Mould, true).Write(b),
            b => new MouldPreferences(
                MouldShapeType.Convex, 3.5f, 8.0f, 2.0f, 3.0f, TroughShapeType.Channels).Write(b),
            b => new DecalPreferences(
                true, DecalAutoPlaceScope.Base, false, DecalAnchor.Top, false, DecalAnchor.Back,
                DecalFont.Bold, 8.0f, 1.2f, EmbossOperation.Emboss).Write(b),
            b => new SmoothingPreferences(3, 2.0f, 0.5f, 1.5f, 2.0f, SmoothDisplayMode.Heatmap).Write(b),
            b => new RotationPreferences(40.0f, 60.0f).Write(b));

        PreferenceProfileIO.Write(_tempFile, bag);
        var result = PreferenceProfileIO.Read(_tempFile);

        Assert.Empty(result.Adjusted);

        var general = GeneralPreferences.Read(result.Bag);
        var printBed = PrintBedPreferences.Read(result.Bag);
        var mould = MouldPreferences.Read(result.Bag);
        var decal = DecalPreferences.Read(result.Bag);
        var smoothing = SmoothingPreferences.Read(result.Bag);
        var rotation = RotationPreferences.Read(result.Bag);

        Assert.Equal(ExportFormat.ThreeMF, general.ExportFormat);
        Assert.Equal(300.0f, printBed.Width);
        Assert.Equal(3.5f, mould.WallThickness);
        Assert.Equal(DecalFont.Bold, decal.Font);
        Assert.Equal(3, smoothing.Iterations);
        Assert.Equal(40.0f, rotation.OverhangWarningAngle);
        Assert.Equal(60.0f, rotation.OverhangCriticalAngle);
    }

    [Fact]
    public void Read_ThrowsOnInvalidJson()
    {
        File.WriteAllText(_tempFile, "{ not valid json: }");
        Assert.Throws<InvalidDataException>(() => PreferenceProfileIO.Read(_tempFile));
    }

    [Fact]
    public void Read_ThrowsOnWrongFormatHeader()
    {
        File.WriteAllText(_tempFile, "{\"format\":\"unrecognized-format\",\"version\":1,\"settings\":{}}");
        Assert.Throws<InvalidDataException>(() => PreferenceProfileIO.Read(_tempFile));
    }

    [Fact]
    public void Read_FallsBackToDefaultsAndLogsAdjustmentsForOutOfRangeNumbers()
    {
        var invalidJson = """
        {
            "format": "fabolus-preferences",
            "version": 1,
            "settings": {
                "smooth_iterations": 999,
                "print_bed_width": -50.0
            }
        }
        """;
        File.WriteAllText(_tempFile, invalidJson);
        var result = PreferenceProfileIO.Read(_tempFile);

        Assert.NotEmpty(result.Adjusted);
        Assert.Equal(SmoothingPreferences.Default.Iterations, SmoothingPreferences.Read(result.Bag).Iterations);
        Assert.Equal(PrintBedPreferences.Default.Width, PrintBedPreferences.Read(result.Bag).Width);

        // The report should name the settings by the label their own section gave them.
        Assert.Contains(result.Adjusted, a => a.StartsWith("Smoothing iterations", StringComparison.Ordinal));
        Assert.Contains(result.Adjusted, a => a.StartsWith("Print bed width", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_ReportsSettingsMissingFromTheFile()
    {
        File.WriteAllText(_tempFile,
            "{\"format\":\"fabolus-preferences\",\"version\":1,\"settings\":{}}");

        var result = PreferenceProfileIO.Read(_tempFile);

        Assert.Contains(result.Adjusted, a => a.Contains("not in file", StringComparison.Ordinal));
        Assert.Equal(MouldPreferences.Default.WallThickness, MouldPreferences.Read(result.Bag).WallThickness);
    }

    [Fact]
    public void Read_RejectsAFileFromANewerFormat()
    {
        File.WriteAllText(_tempFile,
            $"{{\"format\":\"fabolus-preferences\",\"version\":{PreferenceProfileIO.FormatVersion + 1},\"settings\":{{}}}}");

        Assert.Throws<InvalidDataException>(() => PreferenceProfileIO.Read(_tempFile));
    }

    [Fact]
    public void Bag_KeepsKeysItDoesNotRecognise()
    {
        // A newer build's settings must survive a round trip through this one rather than being
        // dropped on the floor.
        var bag = PreferenceBag.FromDefaults();
        bag.Set("a_setting_from_the_future", "keep me");

        PreferenceProfileIO.Write(_tempFile, bag);
        var reread = PreferenceBag.FromJsonObject(
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(_tempFile)).RootElement.GetProperty("settings"));

        Assert.True(reread.ContainsKey("a_setting_from_the_future"));
    }

    [Fact]
    public void Defaults_RoundTripThroughTheBagUnchanged()
    {
        var bag = PreferenceBag.FromDefaults();

        Assert.Equal(GeneralPreferences.Default.ExportFormat, GeneralPreferences.Read(bag).ExportFormat);
        Assert.Equal(PrintBedPreferences.Default, PrintBedPreferences.Read(bag));
        Assert.Equal(CutSplitPreferences.Default, CutSplitPreferences.Read(bag));
        Assert.Equal(DecalPreferences.Default, DecalPreferences.Read(bag));
        Assert.Equal(SmoothingPreferences.Default, SmoothingPreferences.Read(bag));
        Assert.Equal(RotationPreferences.Default, RotationPreferences.Read(bag));
        Assert.Equal(MouldPreferences.Default, MouldPreferences.Read(bag));
    }

    [Fact]
    public void SmoothingPreferences_Clamped_RestrictsOutOfBoundsValues()
    {
        var outOfBounds = new SmoothingPreferences(
            Iterations: 100,
            Intensity: 50.0f,
            Inflation: -5.0f,
            RemeshRatio: 10.0f,
            Resolution: 0.01f,
            DisplayMode: (SmoothDisplayMode)999
        );

        var clamped = outOfBounds.Clamped();

        Assert.Equal(SmoothingPreferences.Ranges.IterationsMax, clamped.Iterations);
        Assert.Equal(SmoothingPreferences.Ranges.IntensityMax, clamped.Intensity);
        Assert.Equal(SmoothingPreferences.Ranges.InflationMin, clamped.Inflation);
        Assert.Equal(SmoothingPreferences.Ranges.RemeshRatioMax, clamped.RemeshRatio);
        Assert.Equal(SmoothingPreferences.Ranges.ResolutionMin, clamped.Resolution);
        Assert.Equal(SmoothingPreferences.Default.DisplayMode, clamped.DisplayMode);
    }

    [Fact]
    public void RotationPreferences_Clamped_RejectsInvertedAngles()
    {
        var inverted = new RotationPreferences(
            OverhangWarningAngle: 70.0f,
            OverhangCriticalAngle: 50.0f
        );

        var clamped = inverted.Clamped();

        Assert.Equal(RotationPreferences.Default.OverhangWarningAngle, clamped.OverhangWarningAngle);
        Assert.Equal(RotationPreferences.Default.OverhangCriticalAngle, clamped.OverhangCriticalAngle);
    }
}
