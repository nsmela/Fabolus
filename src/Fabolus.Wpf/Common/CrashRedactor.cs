using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fabolus.Wpf.Common;

/// <summary>
/// Strips file paths, file names and mesh names out of exception messages before they are
/// written to a crash log or shown. Scans are commonly named after the patient, and a mesh takes
/// its name from the file it was imported from, so either can carry patient information - and a
/// crash log is exactly the thing that gets attached to a public issue.
/// </summary>
/// <remarks>
/// Errs towards removing too much: a message that loses a few words is still useful next to its
/// type and stack, where one that leaks a name cannot be taken back. Stack traces are left alone;
/// they name code, never data.
/// </remarks>
public sealed partial class CrashRedactor {
    public const string PathPlaceholder = "<path>";
    public const string NamePlaceholder = "<name>";

    // Shorter names are left be: redacting every "ab" in a message would leave nothing to read, and
    // a name that short identifies no one.
    public const int MinimumNameLength = 3;

    // Rooted Windows paths (C:\ or C:/) and UNC shares, up to the closing quote or end of line -
    // .NET quotes the paths it puts in messages, and a path may contain spaces. file:// URIs too.
    [GeneratedRegex(@"(?:file:/{2,3}|\\\\[^\\\s'""]+\\|(?<![A-Za-z])[A-Za-z]:[\\/])[^'""\r\n<>|*?]*", RegexOptions.IgnoreCase)]
    private static partial Regex RootedPath();

    // Any quoted text naming a file of a type Fabolus reads or writes, spaces and all.
    [GeneratedRegex(@"(?<q>['""])[^'""\r\n]*\.(?:stl|3mf|obj|off|ply|json)\k<q>", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedFileName();

    // And unquoted, as far back as the last space - a name with spaces in it loses only its end
    // here, which is why the quoted form is taken first.
    [GeneratedRegex(@"[^\s'""\\/:]+\.(?:stl|3mf|obj|off|ply|json)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareFileName();

    private readonly object _gate = new();
    private string[] _names = [];

    /// <summary>
    /// Adds names to redact from now on. Names are kept for the rest of the session, so a mesh that
    /// was deleted a moment before the failure is still covered.
    /// </summary>
    public void AddSensitiveNames(IEnumerable<string> names) {
        lock (_gate) {
            _names = _names
                .Concat(names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()))
                .Where(n => n.Length >= MinimumNameLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                // Longest first, so "Smith (positive)" goes as a whole rather than leaving "(positive)"
                // behind a redacted "Smith"; either way, the name itself is gone.
                .OrderByDescending(n => n.Length)
                .ToArray();
        }
    }

    public string Redact(string? text) {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Names first, then paths: a name can turn up anywhere in a message, not only in a path,
        // and the path scans take whatever is left of a path either way.
        string[] names;
        lock (_gate) names = _names;

        foreach (var name in names) {
            text = text.Replace(name, NamePlaceholder, StringComparison.OrdinalIgnoreCase);
        }

        text = RootedPath().Replace(text, PathPlaceholder);
        text = QuotedFileName().Replace(text, m => $"{m.Groups["q"].Value}{PathPlaceholder}{m.Groups["q"].Value}");
        text = BareFileName().Replace(text, PathPlaceholder);
        return text;
    }
}
