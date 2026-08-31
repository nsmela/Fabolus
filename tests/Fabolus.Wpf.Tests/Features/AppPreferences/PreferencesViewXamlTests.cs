using System;
using System.Collections.Generic;
using System.Threading;
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

    /// <summary>Same dictionaries App.xaml merges, so window resources resolve as they do live.</summary>
    private static readonly string[] ThemeDictionaries = [
        "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
        "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
        "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml",
        "pack://application:,,,/Fabolus;component/Themes/AxisRotationSliderTheme.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Buttons.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Colours.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Controls.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Icons.xaml",
        "pack://application:,,,/Fabolus;component/Themes/SteelSlider.xaml",
        "pack://application:,,,/Fabolus;component/Themes/SteelCyan.xaml",
    ];

    /// <summary>Runs <paramref name="action"/> on an STA thread and rethrows whatever it threw.</summary>
    private static void OnStaThread(Action action) {
        Exception? failure = null;

        var thread = new Thread(() => {
            try {
                // A pack URI resolves its assembly by simple name through Assembly.Load, which
                // only finds one already in the load context. Touch a type first so the
                // Fabolus theme dictionaries can be found.
                _ = typeof(PreferencesView).Assembly;

                if (Application.Current is null) {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var source in ThemeDictionaries) {
                        app.Resources.MergedDictionaries.Add(
                            new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
                    }
                }

                action();
            }
            catch (Exception e) {
                failure = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) {
            throw new Xunit.Sdk.XunitException(
                $"{failure.GetType().Name}: {failure.Message}{Environment.NewLine}{failure}");
        }
    }

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
