using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Microsoft.Win32;
using System.Windows;

namespace Fabolus.Wpf.Common;
public sealed class DialogueSystem : IDialogueSystem {
    public void ShowMessage(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool ShowConfirmation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public Maybe<string> ShowOpenFileDialog(string filter) {
        var dialog = new OpenFileDialog {
            Filter = filter,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? Maybe<string>.Some(dialog.FileName)
            : Maybe<string>.None();
    }

    public Maybe<string> ShowSaveFileDialog(string filter, string defaultExtension) {
        var dialog = new SaveFileDialog {
            Filter = filter,
            DefaultExt = defaultExtension
        };

        return dialog.ShowDialog() == true
            ? Maybe<string>.Some(dialog.FileName)
            : Maybe<string>.None();
    }
}
