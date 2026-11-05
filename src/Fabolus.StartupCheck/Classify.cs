using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fabolus.StartupCheck {

    internal static class Zones {
        public const string NotMarked = "Not marked";
        public const string LocalMachine = "Local machine";

        /// <summary>The URLZONE a Zone.Identifier stream names, as Windows shows it.</summary>
        public static string Name(int zoneId) => zoneId switch {
            0 => LocalMachine,
            1 => "Local intranet",
            2 => "Trusted",
            3 => "Internet",
            4 => "Restricted",
            _ => $"Zone {zoneId}",
        };

        /// <summary>Reads ZoneId out of a Zone.Identifier stream's text; null if it names none.</summary>
        public static int? ParseZoneId(string? stream) {
            if (stream is null) return null;
            var match = Regex.Match(stream, @"^\s*ZoneId\s*=\s*(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
    }

    /// <summary>
    /// Works out what kind of place the Fabolus folder is in, from paths alone, so the rules can be
    /// tested without a network share. The paths themselves never leave this class.
    /// </summary>
    internal static class LocationClassifier {

        // How Explorer ("Temp1_name.zip"), 7-Zip ("7zO...") and WinRAR ("Rar$EX...") name the folder
        // they unpack an archive into when a program is run from inside it.
        private static readonly (Regex Pattern, string Tool)[] Extractors = [
            (new Regex(@"^Temp\d+_.+\.zip$", RegexOptions.IgnoreCase), "Windows Explorer"),
            (new Regex(@"^7zO", RegexOptions.IgnoreCase), "7-Zip"),
            (new Regex(@"^Rar\$EX", RegexOptions.IgnoreCase), "WinRAR"),
        ];

        /// <param name="folder">The folder Fabolus.exe is in.</param>
        /// <param name="knownFolders">Name -> path of the user's known folders (Desktop, Downloads...).</param>
        /// <param name="installRoots">Folders an installer puts programs in.</param>
        /// <param name="driveKind">The kind of drive a "C:\" style root is.</param>
        public static LocationInfo Classify(
            string folder,
            IEnumerable<KeyValuePair<string, string?>> knownFolders,
            string? tempFolder,
            IEnumerable<string?> installRoots,
            Func<string, DriveKind> driveKind) {

            var kind = KindOf(folder, driveKind);

            var under = new List<KnownFolderMatch>();
            foreach (var known in knownFolders) {
                if (string.IsNullOrEmpty(known.Value) || !IsUnder(folder, known.Value!)) continue;
                var knownKind = KindOf(known.Value!, driveKind);
                under.Add(new KnownFolderMatch(known.Key, knownKind is DriveKind.NetworkDrive or DriveKind.UncShare));
            }

            var installed = installRoots.Any(root => !string.IsNullOrEmpty(root) && IsUnder(folder, root!));
            var inTemp = !string.IsNullOrEmpty(tempFolder) && IsUnder(folder, tempFolder!);

            string? extractedBy = null;
            foreach (var segment in Segments(folder)) {
                foreach (var (pattern, tool) in Extractors) {
                    if (pattern.IsMatch(segment)) extractedBy = tool;
                }
            }

            return new LocationInfo(kind, under, installed, inTemp, extractedBy, folder.Length);
        }

        public static DriveKind KindOf(string path, Func<string, DriveKind> driveKind) {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return DriveKind.UncShare;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return DriveKind.UncShare;
            if (path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0])) {
                return driveKind(char.ToUpperInvariant(path[0]) + @":\");
            }
            return DriveKind.Unknown;
        }

        /// <summary>True when <paramref name="path"/> is <paramref name="parent"/> or inside it.</summary>
        public static bool IsUnder(string path, string parent) {
            var p = Normalise(path);
            var root = Normalise(parent);
            if (root.Length == 0) return false;
            return p.Equals(root, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');

        private static IEnumerable<string> Segments(string path) =>
            path.Replace('/', '\\').Split(['\\'], StringSplitOptions.RemoveEmptyEntries);

        public static DriveKind FromDriveType(DriveType type) => type switch {
            DriveType.Fixed => DriveKind.LocalDisk,
            DriveType.Removable => DriveKind.Removable,
            DriveType.Network => DriveKind.NetworkDrive,
            DriveType.CDRom => DriveKind.Optical,
            DriveType.Ram => DriveKind.RamDisk,
            _ => DriveKind.Unknown,
        };

        public static string Describe(DriveKind kind) => kind switch {
            DriveKind.LocalDisk => "local disk",
            DriveKind.Removable => "removable drive (USB stick or card)",
            DriveKind.NetworkDrive => "mapped network drive",
            DriveKind.UncShare => "network share",
            DriveKind.Optical => "CD/DVD",
            DriveKind.RamDisk => "RAM disk",
            _ => "unknown",
        };
    }

    internal static class ModuleClassifier {

        public static ModuleOrigin Classify(
            string modulePath, string appFolder, string? windowsFolder,
            IEnumerable<string?> programFiles, string? programData, string? userProfile) {

            if (LocationClassifier.IsUnder(modulePath, appFolder)) return ModuleOrigin.App;
            if (!string.IsNullOrEmpty(windowsFolder) && LocationClassifier.IsUnder(modulePath, windowsFolder!)) return ModuleOrigin.Windows;
            // The framework-dependent build's runtime, wherever .NET is installed: the frameworks in
            // dotnet\shared, and hostfxr in dotnet\host\fxr.
            var normalised = modulePath.Replace('/', '\\');
            if (normalised.IndexOf(@"\dotnet\shared\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalised.IndexOf(@"\dotnet\host\", StringComparison.OrdinalIgnoreCase) >= 0) return ModuleOrigin.DotNet;
            if (programFiles.Any(root => !string.IsNullOrEmpty(root) && LocationClassifier.IsUnder(modulePath, root!))) return ModuleOrigin.ProgramFiles;
            if (!string.IsNullOrEmpty(programData) && LocationClassifier.IsUnder(modulePath, programData!)) return ModuleOrigin.ProgramData;
            if (!string.IsNullOrEmpty(userProfile) && LocationClassifier.IsUnder(modulePath, userProfile!)) return ModuleOrigin.UserProfile;
            return ModuleOrigin.Other;
        }

        public static string Describe(ModuleOrigin origin) => origin switch {
            ModuleOrigin.App => "fabolus",
            ModuleOrigin.Windows => "windows",
            ModuleOrigin.DotNet => ".net",
            ModuleOrigin.ProgramFiles => "program files",
            ModuleOrigin.ProgramData => "programdata",
            ModuleOrigin.UserProfile => "user profile",
            _ => "other",
        };
    }

    internal static class SecurityCenter {

        /// <summary>
        /// Decodes a Windows Security Center productState: its middle byte says whether the
        /// product is on, its low byte whether its definitions are current.
        /// </summary>
        public static string DescribeState(uint productState) {
            var running = ((productState >> 12) & 0xF) switch {
                0 => "off",
                1 => "on",
                2 => "snoozed",
                3 => "expired",
                var other => $"state {other}",
            };
            var outOfDate = ((productState >> 4) & 0xF) != 0;
            return outOfDate ? running + ", definitions out of date" : running;
        }

        // Process names of endpoint security agents that do not always register with Security Center.
        public static readonly IReadOnlyDictionary<string, string> KnownAgents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["MsSense"] = "Microsoft Defender for Endpoint",
            ["CSFalconService"] = "CrowdStrike Falcon",
            ["SentinelAgent"] = "SentinelOne",
            ["cyserver"] = "Palo Alto Cortex XDR",
            ["RepMgr"] = "VMware Carbon Black",
            ["CylanceSvc"] = "Cylance",
            ["mfemms"] = "Trellix (McAfee)",
            ["masvc"] = "Trellix (McAfee) agent",
            ["SophosFileScanner"] = "Sophos",
            ["SEDService"] = "Sophos",
            ["ccSvcHost"] = "Symantec",
            ["SepMasterService"] = "Symantec Endpoint Protection",
            ["ntrtscan"] = "Trend Micro",
            ["PccNTMon"] = "Trend Micro",
            ["ekrn"] = "ESET",
            ["EPSecurityService"] = "Bitdefender",
            ["bdagent"] = "Bitdefender",
            ["avp"] = "Kaspersky",
            ["elastic-endpoint"] = "Elastic Endpoint",
            ["TaniumClient"] = "Tanium",
        };

        public static IReadOnlyList<string> AgentsIn(IEnumerable<string> processNames) =>
            processNames
                .Select(name => KnownAgents.TryGetValue(name, out var product) ? product : null)
                .Where(product => product is not null)
                .Select(product => product!)
                .Distinct()
                .OrderBy(product => product)
                .ToList();
    }
}
