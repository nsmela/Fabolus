using System;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using ControlzEx.Theming;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fabolus.Wpf;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    public static IHost? AppHost { get; private set; }

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((HostBuilderContext context, IServiceCollection services) =>
            {
                services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

                services.AddSingleton<IAlertDialog, AlertDialog>();
                // Singleton: the store answers every PreferenceSectionRequestMessage, and a
                // RequestMessage throws if a second instance replies to one it already answered.
                services.AddSingleton<AppPreferencesStore>();
                services.AddSingleton<IDialogueSystem, DialogueSystem>();
                services.AddSingleton<IFileSystem, FileSystem>();
                services.AddSingleton<IGeometryEngine>(sp => global::GeometryEngine.BspGeometryEngine.Create());
                services.AddSingleton<Fabolus.Core.Features.Decal.IGlyphOutlineSource, Features.Decal.WpfGlyphOutlineSource>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainView>();

            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        var outlineSource = AppHost.Services.GetRequiredService<Fabolus.Core.Features.Decal.IGlyphOutlineSource>();
        Fabolus.Core.Features.Decal.GlyphOutlineSourceProvider.Default = outlineSource;

        // Nothing injects the store - it answers preference messages. Resolve it here so it is
        // listening before the first view model asks for a section.
        AppHost.Services.GetRequiredService<AppPreferencesStore>();

        // The theme follows the preference live, and starts from whatever was saved.
        var messenger = AppHost.Services.GetRequiredService<IMessenger>();
        messenger.Register<PreferenceSectionUpdateMessage<GeneralPreferences>>(this, (_, msg) => SetTheme(msg.Section.AppTheme));
        SetTheme(messenger.GetSection(GeneralPreferences.Default).AppTheme);

        var mainWindow = AppHost.Services.GetRequiredService<MainView>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    /// <summary>
    /// Switches MahApps' base theme and swaps Fabolus's own override dictionary to match - SteelCyan for
    /// light, FabolusSteelDark for dark. The two define the same keys, so every DynamicResource lookup
    /// follows the swap without anything having to be reloaded.
    /// </summary>
    private void SetTheme(AppTheme theme)
    {
        var isDark = theme == AppTheme.Dark;

        ThemeManager.Current.ChangeTheme(this, isDark ? "Dark.Blue" : "Light.Blue");

        var target = isDark ? "Themes/FabolusSteelDark.xaml" : "Themes/SteelCyan.xaml";
        var current = Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source is not null
            && (d.Source.OriginalString.EndsWith("SteelCyan.xaml") || d.Source.OriginalString.EndsWith("FabolusSteelDark.xaml")));

        if (current is not null && current.Source.OriginalString != target)
        {
            current.Source = new Uri(target, UriKind.Relative);
        }
    }
}
