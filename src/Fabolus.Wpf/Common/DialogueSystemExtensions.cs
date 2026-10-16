using System.IO;
using Fabolus.Core.Common.Interfaces;

namespace Fabolus.Wpf.Common;

public static class DialogueSystemExtensions {
    /// <summary>
    /// Asks before an export replaces files the save dialog never showed - the lettered pieces of
    /// a separated parting split, written beside the name the user picked.
    /// </summary>
    public static bool ConfirmReplacingFiles(this IDialogueSystem dialogue, IReadOnlyList<string> filePaths) {
        var names = string.Join(Environment.NewLine, filePaths.Select(Path.GetFileName));
        var noun = filePaths.Count == 1 ? "This file already exists" : "These files already exist";

        return dialogue.ShowConfirmation(
            "Replace files?",
            $"{noun}:{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}Replace {(filePaths.Count == 1 ? "it" : "them")}?");
    }
}
