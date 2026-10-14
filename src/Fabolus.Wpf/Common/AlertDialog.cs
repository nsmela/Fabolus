using System.Windows;

namespace Fabolus.Wpf.Common;

public interface IAlertDialog {
    void ShowError(string message);
    void ShowInfo(string message);
}

public class AlertDialog : IAlertDialog {
    public void ShowError(string message) {
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    
    public void ShowInfo(string message) {
        MessageBox.Show(message, "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
