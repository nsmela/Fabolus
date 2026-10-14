using System;
using System.Collections.Generic;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.AppPreferences;

/// <summary>
/// Loads the preferences window for real.
///
/// Most of what can go wrong in XAML goes wrong at load time, not build time: a StaticResource
/// key that does not exist, a property that is not on the type, a template that cannot be
/// inflated. The compiler is happy with all three. These run the window through an STA thread so
/// a broken resource reference fails here rather than the first time someone opens Preferences.
/// </summary>
public class PreferencesViewXamlTests {

    /// <summary>
    /// Runs <paramref name="action"/> on the shared XAML host thread and rethrows whatever it
    /// threw. This class used to stand up its own STA thread and Application; see
    /// <see cref="XamlHost"/> for why a second class doing the same broke both.
    /// </summary>
    private static void OnStaThread(Action action) => XamlHost.Run(action);

    private static PreferencesView BuildView() =>
        new(new PreferencesViewModel(new StrongReferenceMessenger(), new Mock<IAlertDialog>().Object));

    [Fact]
    public void TheWindowLoads() {
        OnStaThread(() => {
            var view = BuildView();
            Assert.NotNull(view.Content);
        });
    }

    [Fact]
    public void EveryRowTemplateInflates() {
        // A template is only parsed when something needs it, so a fault inside one stays hidden
        // until that row type appears on screen. Inflate each one to flush them out.
        Type[] rowTypes = [
            typeof(HeaderRow), typeof(NoteRow), typeof(ToggleRow), typeof(NumberRow),
            typeof(SegmentedRow), typeof(DropdownRow), typeof(AnchoredToggleRow), typeof(FolderRow),
        ];

        OnStaThread(() => {
            var view = BuildView();
            var missing = new List<string>();

            foreach (var rowType in rowTypes) {
                if (view.TryFindResource(new DataTemplateKey(rowType)) is not DataTemplate template) {
                    missing.Add(rowType.Name);
                    continue;
                }

                template.LoadContent();
            }

            Assert.Empty(missing);
        });
    }

    [Fact]
    public void TheBespokeOverhangTemplateInflates() {
        OnStaThread(() => {
            var view = BuildView();

            var key = Fabolus.Wpf.Features.Rotatation.RotationPreferencePage.OverhangRangeTemplate;
            var template = view.TryFindResource(key) as DataTemplate;

            Assert.NotNull(template);
            template!.LoadContent();
        });
    }

    [Fact]
    public void TheTemplateSelectorFindsTheBespokeTemplate() {
        OnStaThread(() => {
            var view = BuildView();
            var selector = view.TryFindResource("RowTemplates") as PreferenceRowTemplateSelector;
            Assert.NotNull(selector);

            var custom = new CustomRow {
                TemplateKey = Fabolus.Wpf.Features.Rotatation.RotationPreferencePage.OverhangRangeTemplate,
                Context = new object(),
            };

            Assert.NotNull(selector!.SelectTemplate(custom, view));

            // Everything else falls through to the implicit DataType templates.
            Assert.Null(selector.SelectTemplate(new ToggleRow {
                Read = () => false,
                Write = _ => { },
            }, view));
        });
    }
}
