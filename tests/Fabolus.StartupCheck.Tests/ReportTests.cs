using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fabolus.StartupCheck.Tests;

public sealed class ReportTests
{
    private static StartupReport Report(
        LocationInfo? location = null,
        FolderSurvey? folder = null,
        LaunchResult? launch = null,
        FileAccessProbe? access = null,
        SecuritySurvey? security = null,
        IReadOnlyList<PreviousCheck>? previous = null,
        bool remote = false) => new(
            RunAt: new DateTime(2026, 10, 28, 14, 3, 11),
            CheckerVersion: "1.0.0",
            System: new SystemInfo("Windows 11 Enterprise 24H2 (build 26100.1)", "Some CPU", 8, 16, ["Some GPU (driver 1)"],
                RemoteSession: remote, Elevated: false, DomainJoined: true, FabolusVersion: "1.0.0", Build: "self-contained",
                DesktopRuntimes: []),
            Location: location ?? new LocationInfo(DriveKind.LocalDisk, [], Installed: false, InTemp: false, ExtractedBy: null, PathLength: 20),
            Folder: folder ?? new FolderSurvey(302, 158L << 20, 291, new Dictionary<string, int> { [Zones.NotMarked] = 302 }, 302, 0, 0),
            Launch: launch ?? Launched(window: 3, cpu: 1.7),
            FileAccess: access,
            Signatures: null,
            Security: security ?? new SecuritySurvey([new AntivirusProduct("Windows Defender", "on")], [], [], false, null, "off"),
            PreviousChecks: previous ?? [],
            Steps: [new StepTiming("Listing Fabolus's files", 0.4)]);

    private static LaunchResult Launched(double window, double cpu, params ModuleLoad[] modules)
    {
        var launch = new LaunchResult { WindowSeconds = window, SettledSeconds = window + 0.2, CpuAtWindowSeconds = cpu, ProcessCreatedSeconds = 0.1 };
        launch.Modules.AddRange(modules);
        launch.Samples.Add(new Sample(1, 0.5, 1 << 20, 10, 100, 12));
        return launch;
    }

    [Fact]
    public void A_quick_local_start_finds_nothing()
    {
        Assert.Equal(new[] { Hints.NothingFound }, Hints.For(Report()));
    }

    [Fact]
    public void A_network_folder_points_at_the_installer()
    {
        var hints = Hints.For(Report(location: new LocationInfo(DriveKind.UncShare, [], false, false, null, 40)));

        var hint = Assert.Single(hints);
        Assert.Contains("network share", hint);
        Assert.Contains("installer", hint);
    }

    [Fact]
    public void A_redirected_known_folder_is_named_in_the_hint()
    {
        var location = new LocationInfo(DriveKind.LocalDisk, [new KnownFolderMatch("Desktop", Redirected: true)], false, false, null, 40);

        Assert.Contains(Hints.For(Report(location: location)), h => h.Contains("your Desktop folder"));
    }

    [Fact]
    public void A_slow_start_with_little_cpu_reads_as_waiting()
    {
        var hints = Hints.For(Report(launch: Launched(window: 120, cpu: 6)));

        Assert.Contains(hints, h => h.Contains("waiting rather than working"));
    }

    [Fact]
    public void A_slow_start_with_busy_cpu_reads_as_working()
    {
        var hints = Hints.For(Report(launch: Launched(window: 60, cpu: 50)));

        Assert.Contains(hints, h => h.Contains("busy for most of"));
    }

    [Fact]
    public void Slow_file_opens_are_reported_with_how_many_files_fabolus_loaded()
    {
        var launch = Launched(3, 1, new ModuleLoad(0.1, "Fabolus.dll", ModuleOrigin.App), new ModuleLoad(0.2, "ntdll.dll", ModuleOrigin.Windows));
        var access = new FileAccessProbe(291, MedianOpenMs: 51, P90OpenMs: 70, MaxOpenMs: 190, TotalOpenMs: 15000, Reads: []);

        Assert.Contains(Hints.For(Report(launch: launch, access: access)), h => h.Contains("about 51 ms") && h.Contains("loaded 1 of them"));
    }

    [Fact]
    public void Third_party_security_and_injected_modules_are_named_but_defender_and_dotnet_are_not()
    {
        var launch = Launched(3, 1,
            new ModuleLoad(0.1, "coreclr.dll", ModuleOrigin.DotNet),
            new ModuleLoad(0.2, "umppc.dll", ModuleOrigin.ProgramFiles));
        var security = new SecuritySurvey(
            [new AntivirusProduct("Windows Defender", "on"), new AntivirusProduct("Sophos Anti-Virus", "on")],
            ["CrowdStrike Falcon", "Microsoft Defender for Endpoint"], [], true, true, "off");

        var hints = Hints.For(Report(launch: launch, security: security));

        var securityHint = Assert.Single(hints, h => h.StartsWith("Security software", StringComparison.Ordinal));
        Assert.Contains("Sophos Anti-Virus, CrowdStrike Falcon", securityHint);
        Assert.DoesNotContain("Defender", securityHint);
        var injected = Assert.Single(hints, h => h.StartsWith("Other software loaded", StringComparison.Ordinal));
        Assert.Contains("umppc.dll", injected);
        Assert.DoesNotContain("coreclr", injected);
    }

    [Fact]
    public void An_exit_before_the_window_names_the_crash_log()
    {
        var launch = new LaunchResult { ExitCode = unchecked((int)0xE0434352), ExitedSeconds = 2 };
        launch.NewCrashLogs.Add("crash-20261028-140312-001.txt");

        var hint = Hints.For(Report(launch: launch)).First();

        Assert.Contains("0xE0434352", hint);
        Assert.Contains("crash-20261028-140312-001.txt", hint);
    }

    [Fact]
    public void A_fast_second_check_after_a_slow_first_one_points_at_first_launch_checks()
    {
        var previous = new[] { new PreviousCheck(new DateTime(2026, 10, 28, 13, 0, 0), 54) };

        Assert.Contains(Hints.For(Report(previous: previous)), h => h.Contains("first launch only"));
    }

    [Fact]
    public void Slow_checks_every_time_say_so()
    {
        var previous = new[] { new PreviousCheck(new DateTime(2026, 10, 28, 13, 0, 0), 95) };

        Assert.Contains(Hints.For(Report(launch: Launched(window: 90, cpu: 40), previous: previous)), h => h.Contains("every launch"));
    }

    [Fact]
    public void The_log_carries_no_paths()
    {
        var launch = Launched(3, 1,
            new ModuleLoad(0.1, "Fabolus.exe", ModuleOrigin.App),
            new ModuleLoad(0.2, "ntdll.dll", ModuleOrigin.Windows));
        var location = new LocationInfo(DriveKind.UncShare, [new KnownFolderMatch("Downloads", true)], false, false, null, 80);

        var text = ReportWriter.Format(Report(location: location, launch: launch));

        // The one path the log may hold is the local folder it suggests copying Fabolus to.
        Assert.DoesNotMatch(new Regex(@"[A-Za-z]:\\|\\\\"), text.Replace(@"C:\Fabolus", ""));
    }

    [Fact]
    public void A_log_reads_back_as_an_earlier_check()
    {
        var first = ReportWriter.Format(Report(launch: Launched(window: 54.3, cpu: 3)));
        var failed = ReportWriter.Format(Report(launch: new LaunchResult { TimedOut = true, TimeoutSeconds = 600 }));

        var checks = ReportWriter.ReadPrevious([failed, first, "not a log"]);

        Assert.Equal(2, checks.Count);
        Assert.All(checks, c => Assert.Equal(new DateTime(2026, 10, 28, 14, 3, 11), c.At));
        Assert.Contains(checks, c => c.WindowSeconds == 54.3);
        Assert.Contains(checks, c => c.WindowSeconds is null);
    }

    [Fact]
    public void Gaps_are_measured_from_the_module_before()
    {
        var gaps = ReportWriter.Gaps([
            new ModuleLoad(0.5, "a.dll", ModuleOrigin.App),
            new ModuleLoad(2.0, "b.dll", ModuleOrigin.App),
            new ModuleLoad(2.1, "c.dll", ModuleOrigin.App),
        ]);

        Assert.Equal(new[] { 0.5, 1.5, 0.1 }, gaps.Select(g => Math.Round(g.Gap, 3)));
    }

    [Fact]
    public void Percentiles_pick_from_the_sorted_list()
    {
        double[] sorted = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        Assert.Equal(5, Surveys.Percentile(sorted, 0.5));
        Assert.Equal(9, Surveys.Percentile(sorted, 0.9));
        Assert.Equal(0, Surveys.Percentile([], 0.5));
    }
}
