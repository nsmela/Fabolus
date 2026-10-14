using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Common;

/// <summary>
/// Writes one text file per unexpected exception, so a crash the user reports comes with the
/// stack that caused it rather than a description of what was on screen.
/// </summary>
public sealed class CrashLog {
    public const string FolderName = "logs";

    /// <summary>Next to the preferences file, in per-user application data.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        PreferenceStorageLocation.FolderName,
        FolderName);

    private readonly IFileSystem _fileSystem;
    private readonly Func<DateTime> _clock;
    private readonly string _appVersion;

    public CrashLog(IFileSystem fileSystem, string folder, Func<DateTime> clock, string appVersion, CrashRedactor redactor) {
        _fileSystem = fileSystem;
        Folder = folder;
        _clock = clock;
        _appVersion = appVersion;
        Redactor = redactor;
    }

    public string Folder { get; }

    public CrashRedactor Redactor { get; }

    /// <summary>
    /// Writes the report and returns its path, or null if it could not be written. Never throws:
    /// it runs inside the crash handler, where a second exception would take the app down with it.
    /// </summary>
    public string? Write(Exception exception, string context) {
        try {
            var at = _clock();
            _fileSystem.CreateDirectory(Folder);

            // Several failures can land within the same millisecond (a throw per frame), and a
            // fixed clock in tests lands them all on one - number the later ones instead of
            // overwriting the first.
            var stem = $"crash-{at:yyyyMMdd-HHmmss-fff}";
            var path = Path.Combine(Folder, stem + ".txt");
            for (var n = 2; _fileSystem.Exists(path); n++) {
                path = Path.Combine(Folder, $"{stem}-{n}.txt");
            }

            _fileSystem.WriteAllText(path, Format(exception, context, at, _appVersion, Redactor));
            return path;
        }
        catch (Exception) {
            return null;
        }
    }

    public static string Format(Exception exception, string context, DateTime at, string appVersion, CrashRedactor redactor) {
        var report = new StringBuilder();
        report.AppendLine($"Fabolus {appVersion}");
        report.AppendLine($"Time:    {at:yyyy-MM-dd HH:mm:ss.fff}");
        report.AppendLine($"Context: {context}");
        report.AppendLine($"OS:      {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        report.AppendLine($".NET:    {RuntimeInformation.FrameworkDescription}");
        report.AppendLine();

        AppendChain(report, exception, redactor, depth: 0);
        return report.ToString();
    }

    // Written out by hand rather than with Exception.ToString, which cannot be redacted: it embeds
    // each message as is, and some types add more - FileNotFoundException appends the file name.
    private static void AppendChain(StringBuilder report, Exception exception, CrashRedactor redactor, int depth) {
        report.AppendLine($"{exception.GetType().FullName}: {redactor.Redact(exception.Message)}");
        if (exception.StackTrace is { } stack) report.AppendLine(stack);

        IEnumerable<Exception> inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];

        foreach (var cause in inner) {
            report.AppendLine();
            report.AppendLine($"--- Inner exception (depth {depth + 1}) ---");
            AppendChain(report, cause, redactor, depth + 1);
        }
    }
}
