using System.Windows;

namespace Fabolus.Wpf.Common;

public interface IAlertDialog {
    void ShowError(string message);
}

public class AlertDialog : IAlertDialog {
    public void ShowError(string message) {
        MessageBox.Show(message);
    }
}
