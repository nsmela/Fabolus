using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Fabolus.StartupCheck {

    /// <summary>
    /// The facts gathered from the machine. Each one is best-effort: a standard user on a managed
    /// PC cannot read everything, and a check that cannot see something says so rather than failing.
    /// </summary>
    internal static class Surveys {
        public const string Unavailable = "unavailable";

        private static readonly string[] ProgramExtensions = [".dll", ".exe"];

        public static bool IsProgramFile(string path) =>
            ProgramExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        // --- Machine and Fabolus ----------------------------------------------------------------

        public static SystemInfo Machine(string exePath) {
            var folder = Path.GetDirectoryName(exePath)!;

            string fabolusVersion;
            try { fabolusVersion = FileVersionInfo.GetVersionInfo(exePath).ProductVersion ?? Unavailable; }
            catch (Exception) { fabolusVersion = Unavailable; }

            var build = File.Exists(Path.Combine(folder, "coreclr.dll")) ? "self-contained" : "framework-dependent";

            return new SystemInfo(
                Windows: WindowsVersion(),
                Cpu: Try(() => Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim() ?? Unavailable,
                LogicalProcessors: Environment.ProcessorCount,
                RamGb: RamGb(),
                Gpus: Wmi(@"root\cimv2", "SELECT Name, DriverVersion FROM Win32_VideoController",
                    o => $"{o["Name"]} (driver {o["DriverVersion"]})"),
                RemoteSession: Native.GetSystemMetrics(Native.SmRemoteSession) != 0,
                Elevated: Try(() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)),
                DomainJoined: Wmi(@"root\cimv2", "SELECT PartOfDomain FROM Win32_ComputerSystem", o => (bool?)(bool)o["PartOfDomain"]).FirstOrDefault(),
                FabolusVersion: fabolusVersion,
                Build: build,
                DesktopRuntimes: DesktopRuntimes());
        }

        private static string WindowsVersion() {
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var build = Try(() => Registry.GetValue(key, "CurrentBuild", null) as string);
            var ubr = Try(() => Registry.GetValue(key, "UBR", null)?.ToString());
            var display = Try(() => Registry.GetValue(key, "DisplayVersion", null) as string);
            var edition = Try(() => Registry.GetValue(key, "EditionID", null) as string);
            if (build is null) return RuntimeInformation.OSDescription;

            // ProductName still says "Windows 10" on Windows 11; the build number tells them apart.
            var name = int.TryParse(build, out var number) && number >= 22000 ? "Windows 11" : "Windows 10";
            return $"{name} {edition} {display} (build {build}{(ubr is null ? "" : "." + ubr)})".Replace("  ", " ");
        }

        private static double RamGb() {
            var status = new Native.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Native.MemoryStatusEx>() };
            return Native.GlobalMemoryStatusEx(ref status) ? Math.Round(status.TotalPhys / (1024.0 * 1024 * 1024), 1) : 0;
        }

        private static IReadOnlyList<string> DesktopRuntimes() {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            return Try(() => Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(v => v).ToList()) ?? [];
        }

        // --- Where the folder is ------------------------------------------------------------------

        public static LocationInfo Location(string folder) {
            var known = new List<KeyValuePair<string, string?>> {
                new("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                new("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                new("Downloads", Native.KnownFolderPath(Native.DownloadsFolder)),
                new("OneDrive", Environment.GetEnvironmentVariable("OneDrive")),
                new("OneDrive (work or school)", Environment.GetEnvironmentVariable("OneDriveCommercial")),
            };
            var installRoots = new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            // OneDrive and OneDriveCommercial are often the same folder; report it once.
            var distinct = known
                .Where(k => !string.IsNullOrEmpty(k.Value))
                .GroupBy(k => k.Value!.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            return LocationClassifier.Classify(folder, distinct, Path.GetTempPath(), installRoots, DriveKindOf);
        }

        private static DriveKind DriveKindOf(string root) {
            try { return LocationClassifier.FromDriveType(new DriveInfo(root).DriveType); }
            catch (Exception) { return DriveKind.Unknown; }
        }

        // --- What is in the folder -----------------------------------------------------------------
        //
        // On a network share every per-file call is a round trip to the server, and Fabolus has
        // around 300 files, so these all run after the launch (they would otherwise warm the very
        // caches the launch is timed without) and each one that opens files stops at a budget.

        private const int AttributeOffline = 0x1000;
        private const int AttributePinned = 0x80000;
        private const int AttributeRecallOnOpen = 0x40000;
        private const int AttributeRecallOnDataAccess = 0x400000;

        /// <summary>
        /// Every file in the folder, Fabolus.exe first, then the other programs, then the rest. The
        /// sizes and attributes come with the directory listing, without asking about each file.
        /// </summary>
        public static IReadOnlyList<FileInfo> ListFiles(string folder) {
            var files = Try(() => new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).ToList()) ?? [];
            return files
                .OrderBy(f => f.Name.Equals("Fabolus.exe", StringComparison.OrdinalIgnoreCase) ? 0 : IsProgramFile(f.Name) ? 1 : 2)
                .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static FolderSurvey Folder(IReadOnlyList<FileInfo> files, TimeSpan budget) {
            long bytes = 0;
            int programs = 0, onlineOnly = 0, pinned = 0, zoneChecked = 0;
            var zones = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var clock = Stopwatch.StartNew();

            foreach (var file in files) {
                try {
                    bytes += file.Length;
                    var attributes = (int)file.Attributes;
                    if ((attributes & (AttributeOffline | AttributeRecallOnOpen | AttributeRecallOnDataAccess)) != 0) onlineOnly++;
                    if ((attributes & AttributePinned) != 0) pinned++;
                }
                catch (Exception) { }

                if (IsProgramFile(file.Name)) programs++;

                // Each mark is a file opened on the server; the order puts the ones that matter first.
                if (clock.Elapsed > budget) continue;
                zoneChecked++;
                var zone = ReadZoneId(file.FullName);
                var name = zone is { } id ? Zones.Name(id) : Zones.NotMarked;
                zones[name] = zones.TryGetValue(name, out var n) ? n + 1 : 1;
            }

            return new FolderSurvey(files.Count, bytes, programs, zones, zoneChecked, onlineOnly, pinned);
        }

        /// <summary>The ZoneId in a file's Zone.Identifier stream - the "downloaded" mark - or null.</summary>
        public static int? ReadZoneId(string file) {
            // Opened by hand: .NET Framework's FileStream turns down the "file:stream" form.
            using var handle = Native.CreateFileW(file + ":Zone.Identifier", Native.GenericRead, Native.ShareAll,
                IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) return null;
            try {
                using var stream = new FileStream(handle, FileAccess.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return Zones.ParseZoneId(reader.ReadToEnd());
            }
            catch (Exception) { return null; }
        }

        // --- Signatures and file access -------------------------------------------------------------

        public static SignatureSurvey Signatures(IReadOnlyList<FileInfo> files, TimeSpan budget) {
            // Hashing reads each file whole, so the smallest go first: the most files in the budget.
            var programs = files
                .Where(f => IsProgramFile(f.Name))
                .OrderBy(f => f.Length)
                .Select(f => f.FullName)
                .ToList();

            var signedBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var unsigned = new List<string>();
            var problems = new List<string>();
            var clock = Stopwatch.StartNew();
            var checkedCount = 0;

            foreach (var file in programs) {
                if (clock.Elapsed > budget) break;
                checkedCount++;
                var name = Path.GetFileName(file);
                int result;
                try { result = Native.VerifySignature(file); }
                catch (Exception) { problems.Add($"{name}: could not be checked"); continue; }

                if (result == 0) {
                    var signer = SignerName(file);
                    signedBy[signer] = signedBy.TryGetValue(signer, out var n) ? n + 1 : 1;
                }
                else if (result == Native.TrustNoSignature) unsigned.Add(name);
                else problems.Add($"{name}: 0x{result:X8}");
            }

            unsigned.Sort(StringComparer.OrdinalIgnoreCase);
            return new SignatureSurvey(checkedCount, programs.Count, checkedCount < programs.Count, signedBy, unsigned, problems);
        }

        private static string SignerName(string file) {
            try {
                var subject = X509Certificate.CreateFromSignedFile(file).Subject;
                var cn = subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
                return cn?.Substring(3).Trim('"') ?? subject;
            }
            catch (Exception) { return "unknown signer"; }
        }

        public const int MaxOpensTimed = 100;
        private const long MaxBytesRead = 16L << 20;

        /// <summary>
        /// Times opening the program files (up to <see cref="MaxOpensTimed"/>, or until the budget
        /// runs out), then reads part of the largest one past the file cache. Files Fabolus did not
        /// load go first: Fabolus still holds the ones it loaded open, and on a network share
        /// reopening those is answered from the client's cache, which says nothing about the server.
        /// </summary>
        public static FileAccessProbe ProbeFileAccess(IReadOnlyList<FileInfo> files, TimeSpan budget, IEnumerable<string> loadedByFabolus) {
            var loaded = new HashSet<string>(loadedByFabolus, StringComparer.OrdinalIgnoreCase);
            var programs = files
                .Where(f => IsProgramFile(f.Name))
                .OrderBy(f => loaded.Contains(f.Name) ? 1 : 0)
                .ToList();

            var opens = new List<double>();
            var total = Stopwatch.StartNew();
            foreach (var file in programs.Take(MaxOpensTimed)) {
                if (total.Elapsed > budget) break;
                var clock = Stopwatch.StartNew();
                using var handle = Native.CreateFileW(file.FullName, Native.GenericRead, Native.ShareAll, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
                if (!handle.IsInvalid) opens.Add(clock.Elapsed.TotalMilliseconds);
            }

            var reads = programs
                .OrderByDescending(f => f.Length)
                .Take(1)
                .Select(f => ReadUncached(f, MaxBytesRead))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();

            opens.Sort();
            return new FileAccessProbe(
                Opened: opens.Count,
                MedianOpenMs: Percentile(opens, 0.5),
                P90OpenMs: Percentile(opens, 0.9),
                MaxOpenMs: opens.Count == 0 ? 0 : opens[opens.Count - 1],
                TotalOpenMs: opens.Sum(),
                Reads: reads);
        }

        public static double Percentile(IReadOnlyList<double> sorted, double fraction) {
            if (sorted.Count == 0) return 0;
            var index = (int)Math.Ceiling(fraction * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
        }

        private static ReadSpeed? ReadUncached(FileInfo file, long maxBytes) {
            const int chunk = 1 << 20;
            // Unbuffered reads need a sector-aligned buffer; VirtualAlloc hands out page-aligned ones.
            var buffer = Native.VirtualAlloc(IntPtr.Zero, (UIntPtr)chunk, Native.MemCommitReserve, Native.PageReadWrite);
            if (buffer == IntPtr.Zero) return null;
            try {
                var clock = Stopwatch.StartNew();
                using var handle = Native.CreateFileW(file.FullName, Native.GenericRead, Native.ShareAll, IntPtr.Zero,
                    Native.OpenExisting, Native.FlagNoBuffering | Native.FlagSequentialScan, IntPtr.Zero);
                if (handle.IsInvalid) return null;

                long total = 0;
                while (total < maxBytes && Native.ReadFile(handle, buffer, chunk, out var read, IntPtr.Zero) && read > 0) total += read;

                var seconds = Math.Max(clock.Elapsed.TotalSeconds, 1e-6);
                var megabytes = total / (1024.0 * 1024);
                return new ReadSpeed(file.Name, Math.Round(megabytes, 1), Math.Round(megabytes / seconds, 1));
            }
            finally {
                Native.VirtualFree(buffer, UIntPtr.Zero, Native.MemRelease);
            }
        }

        // --- Security software --------------------------------------------------------------------

        public static SecuritySurvey Security() {
            var antivirus = Wmi(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct",
                o => new AntivirusProduct((string)o["displayName"], SecurityCenter.DescribeState(Convert.ToUInt32(o["productState"]))));

            var agents = SecurityCenter.AgentsIn(Try(() => Process.GetProcesses().Select(p => p.ProcessName).ToList()) ?? []);

            var defender = new List<KeyValuePair<string, string>>();
            AddWmiValues(defender, "SELECT * FROM MSFT_MpComputerStatus", "AMRunningMode", "RealTimeProtectionEnabled", "AMProductVersion");
            AddWmiValues(defender, "SELECT * FROM MSFT_MpPreference",
                "MAPSReporting", "SubmitSamplesConsent", "CloudBlockLevel", "CloudExtendedTimeout", "DisableScanningNetworkFiles");
            if (defender.Count == 0) defender.Add(new("Settings", "not readable without admin rights, or Defender is not in use"));

            var managed = Try(() => {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender");
                // Windows creates the "Policy Manager" subkey on every PC; anything else was set by an admin.
                return (bool?)(key is not null && (key.ValueCount > 0
                    || key.GetSubKeyNames().Any(name => !name.Equals("Policy Manager", StringComparison.OrdinalIgnoreCase))));
            });
            var endpoint = Try(() => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Advanced Threat Protection\Status", "OnboardingState", null) is int state
                ? (bool?)(state == 1)
                : null);

            var smartAppControl = Try(() => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", null)) switch {
                0 => "off",
                1 => "on",
                2 => "evaluation",
                null => Unavailable,
                var other => other.ToString()!,
            };

            return new SecuritySurvey(antivirus, agents, defender, managed, endpoint, smartAppControl);
        }

        private static void AddWmiValues(List<KeyValuePair<string, string>> into, string query, params string[] names) {
            var rows = Wmi(@"root\Microsoft\Windows\Defender", query, o => names
                .Select(n => new KeyValuePair<string, string>(n, Try(() => o[n]?.ToString()) ?? "-"))
                .ToList());
            if (rows.Count > 0) into.AddRange(rows[0]);
        }

        // --- Helpers ---------------------------------------------------------------------------------

        private static IReadOnlyList<T> Wmi<T>(string scope, string query, Func<ManagementBaseObject, T> select) {
            var results = new List<T>();
            try {
                using var searcher = new ManagementObjectSearcher(scope, query);
                foreach (var item in searcher.Get()) {
                    using (item) {
                        try { results.Add(select(item)); } catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            return results;
        }

        private static T? Try<T>(Func<T> read) {
            try { return read(); } catch (Exception) { return default; }
        }
    }
}
