
namespace Fabolus.Core.Common.Interfaces;

/// <summary>
/// Named architectural seam for dialog presentation.
/// Never call MessageBox.Show() or Microsoft.Win32 dialogs directly outside of Infrastructure/.
/// Implementations live in Infrastructure/DialogueSystem.cs.
/// </summary>
public interface IDialogueSystem
{
    /// <summary>
    /// Shows an informational message to the user.
    /// </summary>
    void ShowMessage(string title, string message);

    /// <summary>
    /// Shows a yes/no confirmation dialog.
    /// Returns true if the user confirmed.
    /// </summary>
    bool ShowConfirmation(string title, string message);

    /// <summary>
    /// Shows an open-folder dialog.
    /// Returns the selected folder path, or None if the user cancelled.
    /// </summary>
    Maybe<string> ShowOpenFolderDialogue(string initialDirectory = "");

    /// <summary>
    /// Shows an open-file dialog filtered to the given file type string.
    /// Returns the selected file path, or None if the user cancelled.
    /// </summary>
    /// <param name="filter">Win32-style filter string, e.g. "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"</param>
    Maybe<string> ShowOpenFileDialog(string filter);

    /// <summary>
    /// Shows a save-file dialog filtered to the given file type string.
    /// Returns the selected file path, or None if the user cancelled.
    /// </summary>
    /// <param name="filter">Win32-style filter string, e.g. "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"</param>
    /// <param name="defaultExtension">Extension appended when the user omits one, e.g. ".stl"</param>
    Maybe<string> ShowSaveFileDialog(string filter, string defaultExtension);
}
