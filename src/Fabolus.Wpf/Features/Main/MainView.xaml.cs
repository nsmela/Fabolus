using MahApps.Metro.Controls;
using System.Windows;

namespace Fabolus.Wpf.Features.Main;

/// <summary>
/// Interaction logic for MainView.xaml
/// </summary>
public partial class MainView : MetroWindow {
    public MainView(MainViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
    }

    void OnMinimize(object sender, RoutedEventArgs e)
    => WindowState = WindowState.Minimized;

    void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    void OnClose(object sender, RoutedEventArgs e) => Close();
}

