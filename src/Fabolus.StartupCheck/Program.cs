using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Fabolus.StartupCheck {

    /// <summary>
    /// FabolusStartupCheck.exe: put it beside Fabolus.exe (or drop Fabolus.exe onto it), run it, and
    /// it starts Fabolus, times the start and writes a log to attach to a slow-start issue (#95).
    /// </summary>
    internal static class Program {
        private static readonly TimeSpan SignatureBudget = TimeSpan.FromSeconds(60);

        private static int Main(string[] args) {
            // One number format in the log, whatever the PC's regional settings.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            var options = Options.Parse(args);
            if (options.Error is { } error) {
                Console.WriteLine(error);
                Console.WriteLine(Options.Usage);
                return 2;
            }

            try {
                return Run(options);
            }
            catch (Exception failure) {
                Console.WriteLine();
                Console.WriteLine($"The check itself failed: {failure.GetType().Name}: {failure.Message}");
                Pause(options);
                return 1;
            }
        }

        private static int Run(Options options) {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            Console.WriteLine($"Fabolus startup check {version}");
            Console.WriteLine("This starts Fabolus, times how long it takes to open, and saves a log for your bug report.");
            Console.WriteLine();

            var exePath = FindFabolus(options);
            if (exePath is null) return 2;
            var folder = Path.GetDirectoryName(exePath)!;

            WaitForOtherInstances(options);

            Console.WriteLine("Looking at the Fabolus folder...");
            var system = Surveys.Machine(exePath);
            var location = Surveys.Location(folder);
            var folderSurvey = Surveys.Folder(folder);
            Console.WriteLine($"  Fabolus {system.FabolusVersion} ({system.Build}), {folderSurvey.Files} files, on a {LocationClassifier.Describe(location.Kind)}");

            var logFolder = options.OutputFolder ?? LogFolder();
            Console.WriteLine();
            Console.WriteLine("Starting Fabolus. Leave it open - this window says when it is up.");
            var launch = new LaunchWatcher(exePath, options.Timeout, Console.WriteLine).Run(LogFolder());
            if (launch.WindowSeconds is null) {
                Console.WriteLine(launch.TimedOut
                    ? $"Fabolus's window had not appeared after {options.Timeout.TotalMinutes:0} minutes."
                    : "Fabolus's window did not appear.");
            }
            else {
                Console.WriteLine($"Fabolus finished loading after {launch.SettledSeconds ?? launch.WindowSeconds:0.0} s.");
            }

            Console.WriteLine();
            Console.WriteLine("Checking file access, signatures and security software (this can take a minute)...");
            var access = Surveys.ProbeFileAccess(folder);
            var signatures = Surveys.Signatures(folder, SignatureBudget);
            var security = Surveys.Security();

            var previous = ReportWriter.ReadPrevious(PreviousLogs(logFolder));
            var report = new StartupReport(DateTime.Now, version, system, location, folderSurvey, launch, access, signatures, security, previous);
            var text = ReportWriter.Format(report);

            Directory.CreateDirectory(logFolder);
            var logPath = Path.Combine(logFolder, ReportWriter.FileName(report.RunAt));
            File.WriteAllText(logPath, text);

            Console.WriteLine();
            Console.WriteLine("What this suggests:");
            foreach (var hint in Hints.For(report)) Console.WriteLine($"  - {hint}");
            Console.WriteLine();
            Console.WriteLine("Log saved to:");
            Console.WriteLine($"  {logPath}");
            Console.WriteLine("It holds no file paths, user names or patient data. Please attach it to your GitHub issue.");
            Console.WriteLine("Running the check a second time shows whether only the first launch is slow.");

            if (options.OpenFolder) {
                try { Process.Start("explorer.exe", $"/select,\"{logPath}\""); } catch (Exception) { }
            }
            Pause(options);
            return 0;
        }

        /// <summary>The same folder as Fabolus's crash logs: %LOCALAPPDATA%\Fabolus\logs.</summary>
        private static string LogFolder() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fabolus", "logs");

        private static string[] PreviousLogs(string folder) {
            try {
                return Directory.GetFiles(folder, ReportWriter.FilePrefix + "*.txt")
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .Select(File.ReadAllText)
                    .ToArray();
            }
            catch (Exception) { return []; }
        }

        private static string? FindFabolus(Options options) {
            var candidate = options.Target ?? AppDomain.CurrentDomain.BaseDirectory;
            while (true) {
                candidate = candidate?.Trim().Trim('"');
                if (!string.IsNullOrEmpty(candidate)) {
                    var exe = Directory.Exists(candidate) ? Path.Combine(candidate, "Fabolus.exe") : candidate;
                    if (File.Exists(exe) && Path.GetFileName(exe).Equals("Fabolus.exe", StringComparison.OrdinalIgnoreCase)) {
                        return Path.GetFullPath(exe);
                    }
                }
                if (!options.Interactive) {
                    Console.WriteLine("Fabolus.exe was not found. Pass its path, or put this program in the Fabolus folder.");
                    return null;
                }
                Console.WriteLine("Fabolus.exe is not in this folder. Drag Fabolus.exe onto this window and press Enter");
                Console.Write("(or just press Enter to quit): ");
                candidate = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(candidate)) return null;
            }
        }

        private static void WaitForOtherInstances(Options options) {
            while (Process.GetProcessesByName("Fabolus").Length > 0) {
                Console.WriteLine("Fabolus is already running. A second copy starts faster than the first, so the numbers would be off.");
                if (!options.Interactive) {
                    Console.WriteLine("Carrying on anyway.");
                    return;
                }
                Console.Write("Close Fabolus, then press Enter (or type C and Enter to carry on anyway): ");
                if (string.Equals(Console.ReadLine()?.Trim(), "c", StringComparison.OrdinalIgnoreCase)) return;
            }
        }

        private static void Pause(Options options) {
            if (!options.Interactive) return;
            Console.WriteLine();
            Console.Write("Press Enter to close this window.");
            Console.ReadLine();
        }
    }

    internal sealed class Options {
        public const string Usage =
            "Usage: FabolusStartupCheck [path to Fabolus.exe or its folder] [--timeout <minutes>] [--no-wait] [--out <folder>]\n"
            + "  --timeout  how long to wait for Fabolus's window (default 10 minutes)\n"
            + "  --no-wait  do not ask questions or wait for Enter, and do not open the log's folder\n"
            + "  --out      write the log to this folder instead of %LOCALAPPDATA%\\Fabolus\\logs";

        public string? Target { get; private set; }
        public TimeSpan Timeout { get; private set; } = TimeSpan.FromMinutes(10);
        public bool Interactive { get; private set; } = true;
        public bool OpenFolder => Interactive;
        public string? OutputFolder { get; private set; }
        public string? Error { get; private set; }

        public static Options Parse(string[] args) {
            var options = new Options();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i].ToLowerInvariant()) {
                    case "--timeout" when i + 1 < args.Length
                        && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0:
                        options.Timeout = TimeSpan.FromMinutes(minutes);
                        i++;
                        break;
                    case "--no-wait":
                        options.Interactive = false;
                        break;
                    case "--out" when i + 1 < args.Length:
                        options.OutputFolder = args[++i];
                        break;
                    case "-h" or "--help" or "/?":
                        options.Error = "";
                        break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal) || options.Target is not null) {
                            options.Error = $"Did not understand '{args[i]}'.";
                        }
                        else {
                            options.Target = args[i];
                        }
                        break;
                }
            }
            return options;
        }
    }
}
