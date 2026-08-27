using System;
using System.IO;
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

    [Fact]
    public void WriteAndRead_RoundtripsSuccessfully()
    {
        var profile = new PreferenceProfile
        {
            ImportFolder = Path.GetTempPath(),
            ExportFolder = Path.GetTempPath(),
            ExportFormat = ExportFormat.ThreeMF,
            PrintBedWidth = 300.0f,
            PrintBedDepth = 300.0f,
            ShowBedGrid = false,
            AutodetectChannels = false,
            ChannelDiameter = 6.0f,
            ViewportBackground = ViewportBackground.Graphite,
            SplitViewEnabled = true,
            CutViewEnabled = true,
            CutScope = CutViewScope.Mould,
            MouldShape = MouldShapeType.Convex,
            MouldWallThickness = 3.5f,
            MouldBaseHeight = 8.0f,
            MouldTroughHeight = 2.0f,
            MouldTroughOffset = 3.0f,
            MouldTroughShape = TroughShapeType.Channels,
            DecalsEnabled = true,
            DecalScope = DecalAutoPlaceScope.Base,
            AutoPlaceFilename = false,
            FilenameAnchor = DecalAnchor.Top,
            AutoPlaceVolume = false,
            VolumeAnchor = DecalAnchor.Back,
            DecalFont = DecalFont.Bold,
            DecalCapHeight = 8.0f,
            DecalDepth = 1.2f,
            DecalOperation = EmbossOperation.Emboss,
            SmoothIterations = 3,
            SmoothIntensity = 2.0f,
            SmoothInflation = 0.5f,
            SmoothRemeshRatio = 1.5f,
            SmoothResolution = 2.0f,
            SmoothDisplay = SmoothDisplayMode.Heatmap,
            OverhangWarningAngle = 40.0f,
            OverhangCriticalAngle = 60.0f,
        };

        PreferenceProfileIO.Write(_tempFile, profile);
        var result = PreferenceProfileIO.Read(_tempFile);

        Assert.NotNull(result.Profile);
        Assert.Empty(result.Adjusted);
        Assert.Equal(ExportFormat.ThreeMF, result.Profile.ExportFormat);
        Assert.Equal(300.0f, result.Profile.PrintBedWidth);
        Assert.Equal(3.5f, result.Profile.MouldWallThickness);
        Assert.Equal(DecalFont.Bold, result.Profile.DecalFont);
        Assert.Equal(3, result.Profile.SmoothIterations);
        Assert.Equal(40.0f, result.Profile.OverhangWarningAngle);
        Assert.Equal(60.0f, result.Profile.OverhangCriticalAngle);
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
    public void Read_ClampsAndLogsAdjustmentsForOutOfRangeNumbers()
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

        Assert.NotNull(result.Profile);
        Assert.NotEmpty(result.Adjusted);
        // Out of range smoothing iterations should fall back to default
        Assert.Equal(SmoothingPreferences.Default.Iterations, result.Profile.SmoothIterations);
        // Out of range print bed width should fall back to default
        Assert.Equal(PrintBedPreferences.Default.Width, result.Profile.PrintBedWidth);
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
