using Fabolus.Wpf.Pages.MainWindow;
using Fabolus.Wpf.SplashScreen;
using System.Configuration;
using System.Data;
using System.Windows;

namespace Fabolus.Wpf;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {

    //splash screen
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        var splash = new SplashScreenWindow();
        splash.Show();

        var main = new MainView();
        main.Show();
        splash.Close();
    }
}

