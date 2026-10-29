using System;
using System.Collections.Generic;
using System.Linq;

namespace Fabolus.StartupCheck {

    /// <summary>
    /// Plain-language readings of a report: what the numbers point at, and what to try. Worded as
    /// likely causes, not verdicts - the log goes to a person who can look at the rest of it.
    /// </summary>
    internal static class Hints {
        public const double SlowSeconds = 20;
        public const double WaitingCpuShare = 0.3;
        public const double SlowOpenMs = 20;

        public const string NothingFound =
            "Nothing stands out. If Fabolus still felt slow to start, please attach this log to your issue anyway.";

        public static IReadOnlyList<string> For(StartupReport report) {
            var hints = new List<string>();
            var launch = report.Launch;
            var location = report.Location;

            if (launch.StartError is { } error) hints.Add(error);

            if (launch.ExitCode is { } code && launch.WindowSeconds is null) {
                hints.Add($"Fabolus closed before its window appeared (exit code 0x{code:X8})."
                    + (launch.NewCrashLogs.Count > 0 ? $" It left a crash log: {string.Join(", ", launch.NewCrashLogs)} - please attach that too." : ""));
            }

            if (launch.TimedOut) {
                hints.Add($"Fabolus's window had not appeared after {launch.TimeoutSeconds / 60:0} minutes. Fabolus was left running.");
            }

            if (location.ExtractedBy is { } tool) {
                hints.Add($"Fabolus is being run from inside its zip: {tool} unpacks the whole archive to a temporary folder every time. "
                    + "Extract the zip first (right-click it, Extract All) or use the installer.");
            }

            if (location.OnNetwork) {
                var where = location.Kind is DriveKind.NetworkDrive or DriveKind.UncShare
                    ? $"a {LocationClassifier.Describe(location.Kind)}"
                    : $"your {string.Join(" / ", location.Under.Where(u => u.Redirected).Select(u => u.Name))} folder, which this PC keeps on a network server";
                hints.Add($"Fabolus is running from {where}. Every file it loads is read over the network, and scanned again, on each launch. "
                    + "Use the installer, which puts Fabolus on this PC's own disk, or copy the folder to a local one such as C:\\Fabolus.");
            }
            else if (location.Kind == DriveKind.Removable) {
                hints.Add("Fabolus is running from a removable drive, which is usually much slower than the PC's own disk. Use the installer, or copy the folder to the PC.");
            }

            if (location.Under.Any(u => u.Name.StartsWith("OneDrive", StringComparison.Ordinal)) && report.Folder.CloudOnlineOnly > 0) {
                hints.Add($"{report.Folder.CloudOnlineOnly} of Fabolus's files are online-only OneDrive files, which download when opened. "
                    + "Move Fabolus out of OneDrive, or use the installer.");
            }

            if (report.Folder.MarkedAsDownloaded > 0) {
                var of = report.Folder.ZoneChecked < report.Folder.Files
                    ? $"of the {report.Folder.ZoneChecked} files checked (of {report.Folder.Files})"
                    : $"of {report.Folder.Files} files";
                hints.Add($"{report.Folder.MarkedAsDownloaded} {of} are marked as downloaded from the internet. "
                    + "Antivirus checks each new, unsigned one the first time it loads, which can make a first launch slow. "
                    + "Before extracting a zip, right-click it, choose Properties and tick Unblock.");
            }

            if (launch.WindowSeconds is { } window && window >= SlowSeconds && launch.CpuAtWindowSeconds is { } cpu) {
                var share = cpu / window;
                hints.Add(share < WaitingCpuShare
                    ? $"Fabolus spent most of its {window:0} s start waiting rather than working (it used {cpu:0.0} s of CPU). "
                      + "That points at file access, antivirus scanning or the network, not at Fabolus's own start-up work."
                    : $"Fabolus was busy for most of its {window:0} s start ({cpu:0.0} s of CPU), so the PC itself, or its graphics, is the likelier cause.");
            }

            if (report.FileAccess is { } access && access.MedianOpenMs >= SlowOpenMs) {
                var loaded = launch.Modules.Count(m => m.Origin == ModuleOrigin.App);
                hints.Add($"Opening each of Fabolus's files takes about {access.MedianOpenMs:0} ms here; on a local disk it is well under 1 ms."
                    + (loaded > 0 ? $" Fabolus loaded {loaded} of them while it started." : ""));
            }

            var thirdParty = report.Security.Antivirus
                .Where(a => a.State.StartsWith("on", StringComparison.Ordinal) && a.Name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(a => a.Name)
                .Concat(report.Security.SecurityAgents.Where(a => a.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0))
                .Distinct()
                .ToList();
            if (thirdParty.Count > 0) {
                hints.Add($"Security software is running ({string.Join(", ", thirdParty)}). It may scan each Fabolus file whenever it loads; "
                    + "your IT team can exclude the Fabolus folder, or allow its files, if that is the cause.");
            }

            var injected = launch.Modules
                .Where(m => m.Origin is ModuleOrigin.ProgramFiles or ModuleOrigin.ProgramData or ModuleOrigin.UserProfile or ModuleOrigin.Other)
                .Select(m => m.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (injected.Count > 0) {
                hints.Add($"Other software loaded itself into Fabolus: {string.Join(", ", injected)}. That is usually security, screen-capture or overlay software.");
            }

            if (report.System.RemoteSession) {
                hints.Add("This is a Remote Desktop session, where 3D drawing falls back to the processor and starts more slowly.");
            }

            var earlier = report.PreviousChecks.Where(p => p.WindowSeconds is not null).ToList();
            if (earlier.Count > 0 && launch.WindowSeconds is { } thisTime) {
                var first = earlier.First().WindowSeconds!.Value;
                if (first >= SlowSeconds && thisTime < first / 3) {
                    hints.Add($"An earlier check took {first:0} s and this one {thisTime:0} s, so the slowness is in the first launch only - typical of antivirus checking new files.");
                }
                else if (thisTime >= SlowSeconds && earlier.All(p => p.WindowSeconds >= SlowSeconds)) {
                    hints.Add("Every check on this PC has been slow, so whatever slows it is there on every launch, not only the first.");
                }
            }

            if (hints.Count == 0) hints.Add(NothingFound);
            return hints;
        }
    }
}
