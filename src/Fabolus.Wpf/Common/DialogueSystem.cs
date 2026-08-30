using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Wpf.Features.AppPreferences;
using Microsoft.Win32;
using System.Windows;

namespace Fabolus.Wpf.Common;
public sealed class DialogueSystem : IDialogueSystem {
    private readonly IMessenger _messenger;

    public DialogueSystem(IMessenger messenger) {
        _messenger = messenger;
    }

    public void ShowMessage(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool ShowConfirmation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    private GeneralPreferences General => _messenger.GetSection(GeneralPreferences.Default);

    public Maybe<string> ShowOpenFolderDialogue(string initialDirectory = "") {
        if (string.IsNullOrWhiteSpace(initialDirectory)) {
            initialDirectory = General.ExportFolder;
        }

        var dialog = new OpenFolderDialog {
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog() == true) {
            return Maybe<string>.Some(dialog.FolderName);
        }
        return Maybe<string>.None();
    }

    public Maybe<string> ShowOpenFileDialog(string filter) {
        var defaultFolder = General.ImportFolder;

        var dialog = new OpenFileDialog {
            Filter = filter,
            Multiselect = false,
            InitialDirectory = defaultFolder
        };

        if (dialog.ShowDialog() == true) {
            return Maybe<string>.Some(dialog.FileName);
        }
        return Maybe<string>.None();
    }

    public Maybe<string> ShowSaveFileDialog(string filter, string defaultExtension) {
        var defaultFolder = General.ExportFolder;

        var dialog = new SaveFileDialog {
            Filter = filter,
            DefaultExt = defaultExtension,
            InitialDirectory = defaultFolder
        };

        if (dialog.ShowDialog() == true) {
            return Maybe<string>.Some(dialog.FileName);
        }
        return Maybe<string>.None();
    }
}
