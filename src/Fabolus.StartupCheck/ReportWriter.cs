using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Fabolus.StartupCheck {

    /// <summary>
    /// Turns a report into the text file the user attaches to an issue. The report carries no
    /// paths, so nothing here has to redact; the first lines are what <see cref="ReadPrevious"/>
    /// reads back on later checks.
    /// </summary>
    internal static class ReportWriter {
        public const string FilePrefix = "startup-";
        private const string RunAtLabel = "Run at:";
        private const string WindowLabel = "Window appeared after:";
        private const string RunAtFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Format(StartupReport report) {
            var text = new StringBuilder();
            void Line(string value = "") => text.AppendLine(value);
            void Field(string label, object? value) => text.AppendLine($"{label.PadRight(23)} {value}");
            void Section(string title) { Line(); Line($"== {title}"); }

            var launch = report.Launch;
            Line($"Fabolus startup check {report.CheckerVersion}");
            Field(RunAtLabel, report.RunAt.ToString(RunAtFormat, Invariant));
            Line("Paths, user names and machine names are left out of this log on purpose.");

            Section("Summary");
            Field(WindowLabel, launch.WindowSeconds is { } w ? Seconds(w) : "did not appear");
            if (launch.ProcessCreatedSeconds is { } created) Field("Process created after:", Seconds(created));
            if (launch.SettledSeconds is { } settled) Field("Finished loading after:", Seconds(settled));
            if (launch.CpuAtWindowSeconds is { } cpu && launch.WindowSeconds is { } at && at > 0) {
                Field("CPU used by then:", $"{Seconds(cpu)} ({cpu / at:P0} of the time)");
            }
            if (launch.ReadBytesAtWindow is { } read) Field("File reads by then:", $"{Megabytes(read)} (not counting DLL loads)");
            if (launch.FirstOtherWindowSeconds is { } other) Field("Another window first:", Seconds(other));
            if (launch.ExitCode is { } code) Field("Exited:", $"after {Seconds(launch.ExitedSeconds ?? 0)}, code 0x{code:X8}");
            if (launch.TimedOut) Field("Timed out:", $"after {launch.TimeoutSeconds / 60:0} minutes; Fabolus left running");
            if (launch.StartError is { } error) Field("Could not start:", error);
            if (launch.NewCrashLogs.Count > 0) Field("New crash logs:", string.Join(", ", launch.NewCrashLogs));

            Section("What this suggests");
            foreach (var hint in Hints.For(report)) Line($"- {hint}");

            if (report.PreviousChecks.Count > 0) {
                Section("Earlier checks on this PC");
                foreach (var previous in report.PreviousChecks) {
                    Line($"  {previous.At.ToString(RunAtFormat, Invariant)}   window after {(previous.WindowSeconds is { } s ? Seconds(s) : "-")}");
                }
            }

            Section("Fabolus");
            Field("Version:", report.System.FabolusVersion);
            Field("Build:", report.System.Build);
            Field("Files:", $"{report.Folder.Files} ({Megabytes((ulong)report.Folder.Bytes)}), {report.Folder.ProgramFiles} of them .dll/.exe");

            Section("Where it is");
            var location = report.Location;
            Field("Drive:", LocationClassifier.Describe(location.Kind));
            Field("Inside:", location.Under.Count == 0
                ? "none of Desktop, Documents, Downloads or OneDrive"
                : string.Join(", ", location.Under.Select(u => u.Redirected ? $"{u.Name} (redirected to a network server)" : u.Name)));
            Field("Installed:", location.Installed ? "yes (in a programs folder)" : "no");
            Field("Temporary folder:", location.ExtractedBy is { } tool ? $"yes, unpacked by {tool}" : location.InTemp ? "yes" : "no");
            Field("Path length:", $"{location.PathLength} characters");
            Field("Downloaded marks:", string.Join(", ", report.Folder.ZoneMarks.Select(z => $"{z.Key} {z.Value}")));
            if (report.Folder.CloudOnlineOnly > 0 || report.Folder.CloudPinned > 0) {
                Field("OneDrive files:", $"{report.Folder.CloudOnlineOnly} online-only, {report.Folder.CloudPinned} always kept on this PC");
            }

            Section("This PC");
            Field("Windows:", report.System.Windows);
            Field("Processor:", $"{report.System.Cpu}, {report.System.LogicalProcessors} logical processors");
            Field("Memory:", $"{report.System.RamGb.ToString("0.#", Invariant)} GB");
            foreach (var gpu in report.System.Gpus) Field("Graphics:", gpu);
            Field("Remote Desktop:", YesNo(report.System.RemoteSession));
            Field("Joined to a domain:", report.System.DomainJoined is { } joined ? YesNo(joined) : Surveys.Unavailable);
            Field("Check run as admin:", YesNo(report.System.Elevated));
            Field(".NET Desktop runtimes:", report.System.DesktopRuntimes.Count == 0 ? "none installed" : string.Join(", ", report.System.DesktopRuntimes));

            Section("Security software");
            var security = report.Security;
            if (security.Antivirus.Count == 0) Field("Antivirus:", "none reported by Windows Security Center");
            foreach (var product in security.Antivirus) Field("Antivirus:", $"{product.Name} ({product.State})");
            Field("Security agents:", security.SecurityAgents.Count == 0 ? "none of the known ones" : string.Join(", ", security.SecurityAgents));
            Field("Defender for Endpoint:", security.DefenderForEndpoint is { } mde ? (mde ? "onboarded" : "not onboarded") : "not set up");
            Field("Defender policy:", security.DefenderManagedByPolicy is { } managed ? (managed ? "set by the organisation" : "none") : Surveys.Unavailable);
            foreach (var setting in security.Defender) Field($"Defender {setting.Key}:", setting.Value);
            Field("Smart App Control:", security.SmartAppControl);

            if (report.FileAccess is { } access) {
                Section("File access (after the launch)");
                Field("Files opened:", access.Opened);
                Field("Open time:", $"median {Ms(access.MedianOpenMs)}, 90% under {Ms(access.P90OpenMs)}, slowest {Ms(access.MaxOpenMs)}, total {Ms(access.TotalOpenMs)}");
                foreach (var speed in access.Reads) {
                    Field("Uncached read:", $"{speed.Name}: {speed.Megabytes.ToString("0.0", Invariant)} MB at {speed.MegabytesPerSecond.ToString("0.0", Invariant)} MB/s");
                }
            }

            if (report.Signatures is { } signatures) {
                Section("Signatures");
                Field("Checked:", signatures.RanOutOfTime ? $"{signatures.Checked} of {signatures.Total} (ran out of time)" : $"{signatures.Checked}");
                foreach (var signer in signatures.SignedBy) Field("Signed by:", $"{signer.Key} - {signer.Value}");
                Field("Unsigned:", $"{signatures.Unsigned.Count}");
                if (signatures.Unsigned.Count > 0) Line("  " + string.Join(", ", signatures.Unsigned));
                foreach (var problem in signatures.Problems) Field("Signature problem:", problem);
            }

            if (launch.Samples.Count > 0) {
                Section("Timeline (one row a second)");
                Line("  time      CPU    file reads    reads   modules  threads");
                foreach (var s in launch.Samples) {
                    Line($"{Seconds(s.At),7} {Seconds(s.CpuSeconds),8} {Megabytes(s.ReadBytes),12} {s.ReadOperations,8} {s.Modules,9} {s.Threads,8}");
                }
            }

            if (launch.Modules.Count > 0) {
                Section("Slowest gaps before a module loaded");
                var gaps = Gaps(launch.Modules);
                foreach (var gap in gaps.OrderByDescending(g => g.Gap).Take(10)) {
                    Line($"  {Seconds(gap.Gap),7} before {gap.Module.Name} ({ModuleClassifier.Describe(gap.Module.Origin)}) at {Seconds(gap.Module.At)}");
                }

                Section("Modules in load order");
                Line("   time  origin         module");
                foreach (var module in launch.Modules) {
                    Line($"{Seconds(module.At),7}  {ModuleClassifier.Describe(module.Origin),-13}  {module.Name}");
                }
            }

            return text.ToString();
        }

        public static IReadOnlyList<(ModuleLoad Module, double Gap)> Gaps(IReadOnlyList<ModuleLoad> modules) {
            var gaps = new List<(ModuleLoad, double)>();
            var previous = 0.0;
            foreach (var module in modules) {
                gaps.Add((module, Math.Max(0, module.At - previous)));
                previous = module.At;
            }
            return gaps;
        }

        /// <summary>Earlier checks' run times and window times, read back from their logs, oldest first.</summary>
        public static IReadOnlyList<PreviousCheck> ReadPrevious(IEnumerable<string> logTexts) {
            var checks = new List<PreviousCheck>();
            foreach (var log in logTexts) {
                DateTime? at = null;
                double? window = null;
                foreach (var raw in log.Split('\n').Take(12)) {
                    var line = raw.TrimEnd('\r');
                    if (line.StartsWith(RunAtLabel, StringComparison.Ordinal)
                        && DateTime.TryParseExact(line.Substring(RunAtLabel.Length).Trim(), RunAtFormat, Invariant, DateTimeStyles.None, out var parsed)) {
                        at = parsed;
                    }
                    if (line.StartsWith(WindowLabel, StringComparison.Ordinal)) {
                        var value = line.Substring(WindowLabel.Length).Trim();
                        if (value.EndsWith(" s", StringComparison.Ordinal)
                            && double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, Invariant, out var seconds)) {
                            window = seconds;
                        }
                    }
                }
                if (at is { } when) checks.Add(new PreviousCheck(when, window));
            }
            return checks.OrderBy(c => c.At).ToList();
        }

        public static string FileName(DateTime at) => $"{FilePrefix}{at.ToString("yyyyMMdd-HHmmss", Invariant)}.txt";

        public static string Seconds(double seconds) => seconds.ToString("0.00", Invariant) + " s";

        private static string Ms(double ms) => ms.ToString(ms < 10 ? "0.00" : "0", Invariant) + " ms";

        private static string Megabytes(ulong bytes) => (bytes / (1024.0 * 1024)).ToString("0.0", Invariant) + " MB";

        private static string YesNo(bool value) => value ? "yes" : "no";
    }
}
