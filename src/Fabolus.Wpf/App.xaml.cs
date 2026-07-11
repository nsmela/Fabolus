using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
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
                services.AddTransient<AppPreferencesStore>();
                services.AddSingleton<IDialogueSystem, DialogueSystem>();
                services.AddSingleton<IFileSystem, FileSystem>();
                services.AddSingleton<IGeometryEngine, GeometryEngine>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainView>();

            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        var mainWindow = AppHost.Services.GetRequiredService<MainView>();
        mainWindow.Show();

        base.OnStartup(e);
    }
}

