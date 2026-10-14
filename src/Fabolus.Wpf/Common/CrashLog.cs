using System;
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

    public CrashLog(IFileSystem fileSystem, string folder, Func<DateTime> clock, string appVersion) {
        _fileSystem = fileSystem;
        Folder = folder;
        _clock = clock;
        _appVersion = appVersion;
    }

    public string Folder { get; }

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

            _fileSystem.WriteAllText(path, Format(exception, context, at, _appVersion));
            return path;
        }
        catch (Exception) {
            return null;
        }
    }

    public static string Format(Exception exception, string context, DateTime at, string appVersion) {
        var report = new StringBuilder();
        report.AppendLine($"Fabolus {appVersion}");
        report.AppendLine($"Time:    {at:yyyy-MM-dd HH:mm:ss.fff}");
        report.AppendLine($"Context: {context}");
        report.AppendLine($"OS:      {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        report.AppendLine($".NET:    {RuntimeInformation.FrameworkDescription}");
        report.AppendLine();

        // ToString carries the type, message and stack of the exception and of every inner one,
        // including each exception an AggregateException wraps.
        report.AppendLine(exception.ToString());
        return report.ToString();
    }
}
