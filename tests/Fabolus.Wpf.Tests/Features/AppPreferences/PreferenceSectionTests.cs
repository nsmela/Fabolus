using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Moulding;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.AppPreferences;

public class PreferenceSectionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly AppPreferencesStore _store;
    private readonly PreferencesViewModel _viewModel;

    public PreferenceSectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fabolus_sections_{Guid.NewGuid():N}");
        _path = Path.Combine(_tempDir, "preferences.json");
        _store = new AppPreferencesStore(_messenger, _path);
        _viewModel = new PreferencesViewModel(_messenger, new Mock<IAlertDialog>().Object);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) { Directory.Delete(_tempDir, recursive: true); } } catch { }
    }

    private static IEnumerable<PreferenceRow> RowsOf(PreferencesViewModel vm, IPreferenceSection section) =>
        section.BuildRows(vm);

    public static TheoryData<string> SectionKeys()
    {
        var data = new TheoryData<string>();
        foreach (var section in PreferenceSectionCatalog.Default) { data.Add(section.Key); }
        return data;
    }

    [Fact]
    public void Catalog_IsSortedAndHasUniqueKeys()
    {
        var sections = PreferenceSectionCatalog.Default;

        Assert.Equal(sections.OrderBy(s => s.Order).Select(s => s.Key), sections.Select(s => s.Key));
        Assert.Equal(sections.Select(s => s.Key).Distinct().Count(), sections.Count);
    }

    [Fact]
    public void Catalog_KeepsApplicationPagesAtTheEnds()
    {
        var sections = PreferenceSectionCatalog.Default;

        Assert.Equal("general", sections[0].Key);
        Assert.Equal("appearance", sections[^1].Key);
    }

    [Theory]
    [MemberData(nameof(SectionKeys))]
    public void EverySection_BuildsRowsWithLabels(string key)
    {
        var section = PreferenceSectionCatalog.Default.Single(s => s.Key == key);
        var rows = RowsOf(_viewModel, section);

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
            Assert.True(row is NoteRow || !string.IsNullOrWhiteSpace(row.Label),
                $"A row on '{key}' has no label."));
    }

    [Fact]
    public void EveryToggleRow_RoundTripsThroughTheStore()
    {
        foreach (var section in PreferenceSectionCatalog.Default)
        {
            foreach (var row in RowsOf(_viewModel, section).OfType<ToggleRow>())
            {
                var original = row.Value;
                row.Value = !original;

                Assert.True(row.Value == !original,
                    $"'{row.Label}' on '{section.Key}' did not take the value written to it.");

                row.Value = original;
            }
        }
    }

    [Fact]
    public void EveryNumberRow_RoundTripsAValueInsideItsRange()
    {
        foreach (var section in PreferenceSectionCatalog.Default)
        {
            foreach (var row in RowsOf(_viewModel, section).OfType<NumberRow>())
            {
                var target = Math.Round((row.Minimum + row.Maximum) / 2, 1);
                if (Math.Abs(target - row.Value) < 0.001) { target = row.Minimum; }

                row.Value = target;

                Assert.True(Math.Abs(row.Value - target) < 0.01,
                    $"'{row.Label}' on '{section.Key}' read back {row.Value} after {target} was written.");
            }
        }
    }

    [Fact]
    public void EveryChoiceRow_RoundTripsEachOfItsChoices()
    {
        foreach (var section in PreferenceSectionCatalog.Default)
        {
            foreach (var row in RowsOf(_viewModel, section).OfType<ChoiceRow>())
            {
                Assert.NotEmpty(row.Choices);

                foreach (var choice in row.Choices)
                {
                    row.Value = choice.Value;

                    Assert.True(Equals(row.Value, choice.Value),
                        $"'{row.Label}' on '{section.Key}' would not take '{choice.Label}'.");
                }
            }
        }
    }

    [Fact]
    public void NumberRowLimits_MatchTheRangeTheSettingIsValidatedAgainst()
    {
        // The spinner limits and the stored ranges are the same numbers by construction now.
        // Typing a value the spinner allows must never be silently corrected on the way in.
        var mould = PreferenceSectionCatalog.Default.Single(s => s.Key == "mould");
        var wall = RowsOf(_viewModel, mould).OfType<NumberRow>().Single(r => r.Label == "Wall thickness");

        Assert.Equal(MouldPreferences.Ranges.WallThicknessMin, wall.Minimum);
        Assert.Equal(MouldPreferences.Ranges.WallThicknessMax, wall.Maximum);
    }

    [Fact]
    public void EditingARow_ReachesTheStore()
    {
        var channels = PreferenceSectionCatalog.Default.Single(s => s.Key == "channels");
        var diameter = RowsOf(_viewModel, channels).OfType<NumberRow>().Single();

        diameter.Value = 6.5;

        Assert.Equal(6.5f, _store.Get<PrintBedPreferences>().ChannelDiameter);
    }

    [Fact]
    public void TroughRows_AreDisabledForAContouredMould()
    {
        var mould = PreferenceSectionCatalog.Default.Single(s => s.Key == "mould");
        var rows = RowsOf(_viewModel, mould);
        var troughDepth = rows.OfType<NumberRow>().Single(r => r.Label == "Depth");

        _viewModel.MouldShape = MouldShapeType.Concave;
        Assert.True(troughDepth.IsEnabled);

        _viewModel.MouldShape = MouldShapeType.Contoured;
        Assert.False(troughDepth.IsEnabled);
    }

    [Fact]
    public void DecalDefaults_AreDisabledWhileTheToolIsOff()
    {
        var decals = PreferenceSectionCatalog.Default.Single(s => s.Key == "decals");
        var rows = RowsOf(_viewModel, decals);
        var master = rows.OfType<ToggleRow>().Single(r => r.Label == "Decal tool");
        var capHeight = rows.OfType<NumberRow>().Single(r => r.Label == "Cap height");

        master.Value = true;
        Assert.True(capHeight.IsEnabled);

        master.Value = false;
        Assert.False(capHeight.IsEnabled);
        // The master switch itself always stays reachable, or it could not be turned back on.
        Assert.True(master.IsEnabled);
    }

    [Fact]
    public void CutScope_IsDisabledWhileTheCutViewIsOff()
    {
        var cut = PreferenceSectionCatalog.Default.Single(s => s.Key == "cut");
        var rows = RowsOf(_viewModel, cut);
        var enable = rows.OfType<ToggleRow>().Single();
        var scope = rows.OfType<DropdownRow>().Single();

        enable.Value = false;
        Assert.False(scope.IsEnabled);

        enable.Value = true;
        Assert.True(scope.IsEnabled);
    }

    [Fact]
    public void Search_FiltersTheSidebarAndKeepsAPageSelected()
    {
        _viewModel.SearchText = "trough";

        Assert.Contains(_viewModel.Sections, s => s.Key == "mould");
        Assert.DoesNotContain(_viewModel.Sections, s => s.Key == "decals");
        Assert.NotNull(_viewModel.SelectedSection);
        Assert.Contains(_viewModel.Sections, s => s == _viewModel.SelectedSection);

        _viewModel.SearchText = string.Empty;
        Assert.Equal(PreferenceSectionCatalog.Default.Count, _viewModel.Sections.Count);
    }

    [Fact]
    public void SelectingASection_ExposesItsRows()
    {
        _viewModel.SelectedSection = _viewModel.Sections.Single(s => s.Key == "split");

        Assert.Single(_viewModel.Rows);
        Assert.Equal("Split view (for moulds)", _viewModel.Rows[0].Label);
    }

    [Fact]
    public void RestoringDefaults_IsVisibleOnTheRows()
    {
        _viewModel.SelectedSection = _viewModel.Sections.Single(s => s.Key == "channels");
        var diameter = _viewModel.Rows.OfType<NumberRow>().Single();

        diameter.Value = 9.0;
        Assert.Equal(9.0, diameter.Value);

        _viewModel.RestoreDefaultsCommand.Execute(null);

        Assert.Equal(PrintBedPreferences.Default.ChannelDiameter, diameter.Value);
    }

    [Fact]
    public void CustomRow_NamesATemplateAndCarriesTheViewModel()
    {
        var rotation = PreferenceSectionCatalog.Default.Single(s => s.Key == "rotation");
        var custom = RowsOf(_viewModel, rotation).OfType<CustomRow>().Single();

        Assert.Equal(Fabolus.Wpf.Features.Rotatation.RotationPreferenceSection.OverhangRangeTemplate,
            custom.TemplateKey);
        Assert.Same(_viewModel, custom.Context);
    }
}
