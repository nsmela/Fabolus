using MahApps.Metro.Controls;

namespace Fabolus.Wpf.Features.AppPreferences;

public partial class PreferencesView : MetroWindow {
    public PreferencesView(PreferencesViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Settings are persisted live (each view-model property change sends a
    // message the AppPreferencesStore saves), so both buttons simply close.
    private void OnSaveClick(object sender, System.Windows.RoutedEventArgs e) => Close();

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e) => Close();
}
