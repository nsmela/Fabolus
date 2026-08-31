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
using GeometryMeshLib;
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
                services.AddSingleton<IGeometryEngine, GeometryEngine>();
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

        // 1. Hook up dynamic theme switching
        var messenger = AppHost.Services.GetRequiredService<IMessenger>();
        messenger.Register<PreferenceSectionUpdateMessage<GeneralPreferences>>(this, (_, msg) =>
        {
            SetTheme(msg.Section.AppTheme);
        });

        // 2. Set initial theme
        var general = messenger.GetSection(GeneralPreferences.Default);
        SetTheme(general.AppTheme);

        var mainWindow = AppHost.Services.GetRequiredService<MainView>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    private void SetTheme(AppTheme theme)
    {
        var isDark = theme == AppTheme.Dark;
        
        // 1. Change MahApps theme
        ThemeManager.Current.ChangeTheme(this, isDark ? "Dark.Blue" : "Light.Blue");

        // 2. Change our custom theme override
        var targetThemePath = isDark ? "Themes/FabolusSteelDark.xaml" : "Themes/SteelCyan.xaml";
        
        var existingDict = Resources.MergedDictionaries.FirstOrDefault(d => 
            d.Source is not null && (d.Source.OriginalString.EndsWith("SteelCyan.xaml") || d.Source.OriginalString.EndsWith("FabolusSteelDark.xaml")));
            
        if (existingDict is not null && existingDict.Source.OriginalString != targetThemePath)
        {
            existingDict.Source = new Uri(targetThemePath, UriKind.Relative);
        }
    }
}
