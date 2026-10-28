using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Fabolus.StartupCheck {

    /// <summary>
    /// Starts Fabolus the way a double-click does and watches it from outside until its main window
    /// is up: when each module loads, and how much CPU and file reading the process used on the way.
    /// It never closes Fabolus - the user carries on with it once the check is done.
    /// </summary>
    internal sealed class LaunchWatcher {
        public const string MainWindowTitle = "FABOLUS";

        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan QuietToSettle = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MaxSettle = TimeSpan.FromSeconds(20);

        private readonly string _exePath;
        private readonly string _folder;
        private readonly TimeSpan _timeout;
        private readonly Action<string> _progress;
        private readonly string? _windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private readonly string?[] _programFiles = [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        private readonly string? _programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        private readonly string? _profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public LaunchWatcher(string exePath, TimeSpan timeout, Action<string> progress) {
            _exePath = exePath;
            _folder = Path.GetDirectoryName(exePath)!;
            _timeout = timeout;
            _progress = progress;
        }

        public LaunchResult Run(string crashLogFolder) {
            var result = new LaunchResult { TimeoutSeconds = _timeout.TotalSeconds };
            var crashLogsBefore = CrashLogs(crashLogFolder);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var launchedAt = DateTime.Now;
            var clock = Stopwatch.StartNew();
            Process? process;
            try {
                // Through the shell, as a double-click is: SmartScreen and the download checks apply.
                process = Process.Start(new ProcessStartInfo(_exePath) { UseShellExecute = true, WorkingDirectory = _folder });
            }
            catch (Win32Exception failure) when (failure.NativeErrorCode == 1223) {
                result.StartError = "The launch was cancelled (for example at a Windows SmartScreen prompt).";
                return result;
            }
            catch (Exception failure) {
                result.StartError = $"Fabolus could not be started: {failure.GetType().Name} (0x{failure.HResult:X8}).";
                return result;
            }
            result.StartReturnedSeconds = clock.Elapsed.TotalSeconds;
            if (process is null) {
                result.StartError = "Windows started Fabolus without handing back the process, so it could not be watched.";
                return result;
            }

            using (process) {
                try { result.ProcessCreatedSeconds = Math.Max(0, (process.StartTime - launchedAt).TotalSeconds); } catch (Exception) { }

                var nextSample = TimeSpan.Zero;
                var nextProgress = TimeSpan.FromSeconds(5);
                TimeSpan? windowAt = null;
                var lastNewModule = TimeSpan.Zero;

                while (true) {
                    var now = clock.Elapsed;
                    try { process.Refresh(); } catch (Exception) { }

                    if (process.HasExited) {
                        result.ExitedSeconds = now.TotalSeconds;
                        try { result.ExitCode = process.ExitCode; } catch (Exception) { }
                        break;
                    }

                    if (RecordNewModules(process, seen, now, result)) lastNewModule = now;

                    if (now >= nextSample) {
                        result.Samples.Add(SampleOf(process, now, seen.Count));
                        nextSample = now + SampleEvery;
                    }

                    if (windowAt is null) {
                        var title = MainWindowTitleOf(process);
                        if (title is not null) {
                            if (title.Equals(MainWindowTitle, StringComparison.OrdinalIgnoreCase)) {
                                windowAt = now;
                                result.WindowSeconds = now.TotalSeconds;
                                var sample = SampleOf(process, now, seen.Count);
                                result.CpuAtWindowSeconds = sample.CpuSeconds;
                                result.ReadBytesAtWindow = sample.ReadBytes;
                                _progress($"Fabolus's window appeared after {now.TotalSeconds:0.0} s. Waiting for it to finish loading...");
                            }
                            else {
                                // A dialog first: the startup error box, or something else's prompt.
                                result.FirstOtherWindowSeconds ??= now.TotalSeconds;
                            }
                        }

                        if (now >= _timeout) {
                            result.TimedOut = true;
                            break;
                        }
                        if (now >= nextProgress) {
                            var last = result.Samples.LastOrDefault();
                            _progress($"  {now.TotalSeconds,5:0} s   {seen.Count,3} modules loaded   CPU {last?.CpuSeconds ?? 0:0.0} s");
                            nextProgress = now + TimeSpan.FromSeconds(5);
                        }
                    }
                    else if (now - lastNewModule >= QuietToSettle || now - windowAt.Value >= MaxSettle) {
                        // Loaded when no new module has turned up for a while: the 3D view loads
                        // its DirectX libraries a moment after the window shows.
                        result.SettledSeconds = (lastNewModule > windowAt.Value ? lastNewModule : windowAt.Value).TotalSeconds;
                        result.Samples.Add(SampleOf(process, now, seen.Count));
                        break;
                    }

                    Thread.Sleep(Poll);
                }
            }

            result.NewCrashLogs.AddRange(CrashLogs(crashLogFolder).Except(crashLogsBefore, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        private bool RecordNewModules(Process process, HashSet<string> seen, TimeSpan now, LaunchResult result) {
            var found = false;
            try {
                foreach (ProcessModule module in process.Modules) {
                    using (module) {
                        var path = module.FileName;
                        if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                        var origin = ModuleClassifier.Classify(path, _folder, _windows, _programFiles, _programData, _profile);
                        result.Modules.Add(new ModuleLoad(now.TotalSeconds, Path.GetFileName(path), origin));
                        found = true;
                    }
                }
            }
            // The module list cannot be read while the process is still being set up, or as it exits.
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            return found;
        }

        private static Sample SampleOf(Process process, TimeSpan now, int modules) {
            double cpu = 0;
            int threads = 0;
            Native.IoCounters io = default;
            try { cpu = process.TotalProcessorTime.TotalSeconds; } catch (Exception) { }
            try { threads = process.Threads.Count; } catch (Exception) { }
            try { Native.GetProcessIoCounters(process.Handle, out io); } catch (Exception) { }
            return new Sample(now.TotalSeconds, cpu, io.ReadTransferCount, io.ReadOperationCount, modules, threads);
        }

        private static string? MainWindowTitleOf(Process process) {
            try {
                if (process.MainWindowHandle == IntPtr.Zero) return null;
                var title = process.MainWindowTitle;
                return string.IsNullOrEmpty(title) ? null : title;
            }
            catch (Exception) { return null; }
        }

        private static IReadOnlyList<string> CrashLogs(string folder) {
            try { return Directory.GetFiles(folder, "crash-*.txt").Select(Path.GetFileName).ToList(); }
            catch (Exception) { return []; }
        }
    }
}
