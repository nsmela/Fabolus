using CommunityToolkit.Mvvm.Messaging;
using BasicResults;
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

    public Maybe<string> ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName = "") {
        var defaultFolder = General.ExportFolder;

        var dialog = new SaveFileDialog {
            Filter = filter,
            DefaultExt = defaultExtension,
            InitialDirectory = defaultFolder,
            FileName = SanitiseFileName(defaultFileName)
        };

        if (dialog.ShowDialog() == true) {
            return Maybe<string>.Some(dialog.FileName);
        }
        return Maybe<string>.None();
    }

    // A mesh name is not guaranteed to be a legal filename: it comes from whatever the engine
    // read out of the file, and for a multi-component 3MF that is a component name the format
    // lets contain anything at all. SaveFileDialog does not reject an illegal name up front - it
    // shows it, and the user only finds out when saving fails - so the offending characters are
    // dropped here. An empty result leaves the dialog blank, which is the old behaviour.
    private static string SanitiseFileName(string name) {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var cleaned = new string(name
            .Where(c => !System.IO.Path.GetInvalidFileNameChars().Contains(c))
            .ToArray());

        return cleaned.Trim();
    }
}
