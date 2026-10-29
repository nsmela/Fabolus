using System;
using System.Collections.Generic;
using System.Linq;

// Records need IsExternalInit, which .NET Framework does not ship.
namespace System.Runtime.CompilerServices {
    internal static class IsExternalInit { }
}

namespace Fabolus.StartupCheck {

    /// <summary>
    /// Everything one check found. Holds no paths, user names or machine names - only categories,
    /// counts, times and the file names of Fabolus's own files - so the log it becomes can be
    /// attached to a public issue as is.
    /// </summary>
    internal sealed record StartupReport(
        DateTime RunAt,
        string CheckerVersion,
        SystemInfo System,
        LocationInfo Location,
        FolderSurvey Folder,
        LaunchResult Launch,
        FileAccessProbe? FileAccess,
        SignatureSurvey? Signatures,
        SecuritySurvey Security,
        IReadOnlyList<PreviousCheck> PreviousChecks,
        // How long each of the check's own steps took: slow ones are evidence too.
        IReadOnlyList<StepTiming> Steps);

    internal sealed record StepTiming(string Step, double Seconds);

    internal sealed record SystemInfo(
        string Windows,
        string Cpu,
        int LogicalProcessors,
        double RamGb,
        IReadOnlyList<string> Gpus,
        bool RemoteSession,
        bool Elevated,
        bool? DomainJoined,
        string FabolusVersion,
        string Build,
        IReadOnlyList<string> DesktopRuntimes);

    internal enum DriveKind { LocalDisk, Removable, NetworkDrive, UncShare, Optical, RamDisk, Unknown }

    /// <summary>A known folder (Desktop, Downloads, OneDrive...) the Fabolus folder is inside.</summary>
    internal sealed record KnownFolderMatch(string Name, bool Redirected);

    internal sealed record LocationInfo(
        DriveKind Kind,
        IReadOnlyList<KnownFolderMatch> Under,
        bool Installed,
        bool InTemp,
        // What unpacked the folder into Temp, when that shows in its name: run from inside a zip.
        string? ExtractedBy,
        int PathLength) {

        public bool OnNetwork => Kind is DriveKind.NetworkDrive or DriveKind.UncShare
            || Under.Any(m => m.Redirected);
    }

    internal sealed record FolderSurvey(
        int Files,
        long Bytes,
        int ProgramFiles,
        // Zone name -> files carrying it; "Not marked" for files without a mark.
        IReadOnlyDictionary<string, int> ZoneMarks,
        // How many files were checked for a mark before the time ran out.
        int ZoneChecked,
        int CloudOnlineOnly,
        int CloudPinned) {

        public int MarkedAsDownloaded => ZoneMarks
            .Where(pair => pair.Key is not (Zones.NotMarked or Zones.LocalMachine))
            .Sum(pair => pair.Value);
    }

    internal enum ModuleOrigin { App, Windows, DotNet, ProgramFiles, ProgramData, UserProfile, Other }

    internal sealed record ModuleLoad(double At, string Name, ModuleOrigin Origin);

    internal sealed record Sample(double At, double CpuSeconds, ulong ReadBytes, ulong ReadOperations, int Modules, int Threads);

    internal sealed class LaunchResult {
        public string? StartError { get; set; }
        public double? StartReturnedSeconds { get; set; }
        public double? ProcessCreatedSeconds { get; set; }
        public double? FirstOtherWindowSeconds { get; set; }
        public double? WindowSeconds { get; set; }
        public double? SettledSeconds { get; set; }
        public double? CpuAtWindowSeconds { get; set; }
        public ulong? ReadBytesAtWindow { get; set; }
        public int? ExitCode { get; set; }
        public double? ExitedSeconds { get; set; }
        public bool TimedOut { get; set; }
        public double TimeoutSeconds { get; set; }
        public List<Sample> Samples { get; } = [];
        public List<ModuleLoad> Modules { get; } = [];
        public List<string> NewCrashLogs { get; } = [];
    }

    internal sealed record FileAccessProbe(
        int Opened,
        double MedianOpenMs,
        double P90OpenMs,
        double MaxOpenMs,
        double TotalOpenMs,
        IReadOnlyList<ReadSpeed> Reads);

    internal sealed record ReadSpeed(string Name, double Megabytes, double MegabytesPerSecond);

    internal sealed record SignatureSurvey(
        int Checked,
        int Total,
        bool RanOutOfTime,
        IReadOnlyDictionary<string, int> SignedBy,
        IReadOnlyList<string> Unsigned,
        IReadOnlyList<string> Problems);

    internal sealed record AntivirusProduct(string Name, string State);

    internal sealed record SecuritySurvey(
        IReadOnlyList<AntivirusProduct> Antivirus,
        IReadOnlyList<string> SecurityAgents,
        IReadOnlyList<KeyValuePair<string, string>> Defender,
        bool? DefenderManagedByPolicy,
        bool? DefenderForEndpoint,
        string SmartAppControl);

    internal sealed record PreviousCheck(DateTime At, double? WindowSeconds);
}
