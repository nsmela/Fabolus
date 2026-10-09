using MahApps.Metro.Controls;

namespace Fabolus.Wpf.Features.About;

public partial class AboutView : MetroWindow {
    public AboutView(AboutViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, System.Windows.RoutedEventArgs e) => Close();
}
