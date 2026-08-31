using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.AppPreferences;

/// <summary>
/// A feature's own view and its preference page have to offer the same range.
///
/// They were separate literals once, and drifted: the smooth view let intensity reach 20mm while
/// a smoothing default was validated against 3mm, so a user could set a value in the tool that no
/// default could express - and a stored default outside the narrower range was discarded on the
/// way in without saying so. The views bind to the ranges now; this is what keeps that true.
///
/// Deliberately works on the view types and the markup, never on an instance: constructing a
/// feature view model spins up a scene manager and its 3D visuals, which is far more than these
/// assertions need.
/// </summary>
public class SliderBoundsTests {

    /// <summary>Repository path of a view, resolved from the test assembly's location.</summary>
    private static string ViewPath(string relative) {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Fabolus.Wpf", "Features", relative);
    }

    /// <summary>Every limit in a view, paired with the value its control edits.</summary>
    private static IEnumerable<(string Limit, string Value)> Limits(string relative) {
        var xaml = File.ReadAllText(ViewPath(relative));

        foreach (Match control in Regex.Matches(xaml, @"<(?:Slider|mah:NumericUpDown|mah:RangeSlider)\b[^>]*>",
                                                RegexOptions.Singleline)) {
            var value = Regex.Match(control.Value, @"(?:Value|LowerValue)=""\{Binding (\w+)");
            if (!value.Success) { continue; }

            foreach (Match limit in Regex.Matches(control.Value, @"\b(?:Minimum|Maximum|MinRange)=""([^""]+)""")) {
                yield return (limit.Groups[1].Value, value.Groups[1].Value);
            }
        }
    }

    /// <summary>Each view, the view model behind it, and the values a preference supplies a default for.</summary>
    public static TheoryData<string, Type, string[]> PreferenceBackedSliders => new() {
        { "Smoothing/SmoothingView.xaml",  typeof(SmoothingViewModel),
          ["Intensity", "Inflation", "Iterations", "RemeshRatio", "Resolution"] },
        { "Rotatation/RotateView.xaml",    typeof(RotateViewModel),  ["WarningAngle"] },
        { "Moulding/MouldControl.xaml",    typeof(MouldViewModel),
          ["WallThickness", "BaseHeight", "TroughHeight", "TroughOffset"] },
        { "Moulding/ChannelsControl.xaml", typeof(MouldViewModel),   ["ChannelDiameter"] },
        { "Emboss/EmbossView.xaml",        typeof(DecalViewModel),   ["CapHeight", "Depth"] },
    };

    [Theory]
    [MemberData(nameof(PreferenceBackedSliders))]
    public void PreferenceBackedSliders_BindTheirLimits(string view, Type viewModel, string[] values) {
        var hardcoded = Limits(view)
            .Where(l => values.Contains(l.Value))
            .Where(l => !l.Limit.StartsWith("{Binding", StringComparison.Ordinal))
            .Select(l => $"{l.Value} has a hardcoded limit of {l.Limit}")
            .ToList();

        Assert.True(hardcoded.Count == 0,
            $"{view}: a preference-backed slider must bind its limits, or the two can drift apart again."
            + Environment.NewLine + string.Join(Environment.NewLine, hardcoded));
    }

    [Theory]
    [MemberData(nameof(PreferenceBackedSliders))]
    public void EveryBoundLimit_ResolvesToAPropertyOnTheViewModel(string view, Type viewModel, string[] values) {
        var unresolved = Limits(view)
            .Where(l => values.Contains(l.Value))
            .Select(l => Regex.Match(l.Limit, @"^\{Binding (\w+)\}$"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Where(name => viewModel.GetProperty(name) is null)
            .ToList();

        Assert.True(unresolved.Count == 0,
            $"{view} binds limits that {viewModel.Name} does not expose: {string.Join(", ", unresolved)}");
    }

    /// <summary>
    /// The gap the overhang slider enforces has to be the one the section rejects below it, or a
    /// drag the control permits is thrown away by the store on its way to disk - leaving the
    /// window showing a pair that was never saved.
    /// </summary>
    [Fact]
    public void TheTightestOverhangPairTheSliderAllows_SurvivesBeingStored() {
        float warning = RotationPreferences.Ranges.OverhangAngleMin;
        float critical = warning + RotationPreferences.Ranges.OverhangMinGap;

        var tightest = new RotationPreferences(warning, critical);

        Assert.Equal(tightest, tightest.Clamped());
    }

    /// <summary>And the widest, so neither end of the slider's travel is rejected either.</summary>
    [Fact]
    public void TheWidestOverhangPairTheSliderAllows_SurvivesBeingStored() {
        var widest = new RotationPreferences(
            RotationPreferences.Ranges.OverhangAngleMin,
            RotationPreferences.Ranges.OverhangAngleMax);

        Assert.Equal(widest, widest.Clamped());
    }
}
