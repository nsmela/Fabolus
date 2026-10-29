using System;
using System.Collections.Generic;
using Xunit;

namespace Fabolus.StartupCheck.Tests;

public sealed class LocationClassifierTests
{
    private const string Profile = @"C:\Users\pat";
    private const string Temp = @"C:\Users\pat\AppData\Local\Temp\";

    private static readonly string?[] InstallRoots = [@"C:\Users\pat\AppData\Local\Programs", @"C:\Program Files"];

    // C: is the PC's disk, H: a mapped home drive.
    private static DriveKind Drives(string root) => root switch
    {
        @"C:\" => DriveKind.LocalDisk,
        @"H:\" => DriveKind.NetworkDrive,
        @"E:\" => DriveKind.Removable,
        _ => DriveKind.Unknown,
    };

    private static LocationInfo Classify(string folder, params KeyValuePair<string, string?>[] known) =>
        LocationClassifier.Classify(folder, known, Temp, InstallRoots, Drives);

    private static KeyValuePair<string, string?> Known(string name, string path) => new(name, path);

    [Fact]
    public void A_folder_on_the_local_disk_is_not_on_the_network()
    {
        var location = Classify(@"C:\Fabolus");

        Assert.Equal(DriveKind.LocalDisk, location.Kind);
        Assert.False(location.OnNetwork);
        Assert.False(location.Installed);
        Assert.Null(location.ExtractedBy);
    }

    [Theory]
    [InlineData(@"\\fileserver\home$\pat\Fabolus")]
    [InlineData(@"\\?\UNC\fileserver\home$\pat\Fabolus")]
    public void A_unc_path_is_a_network_share(string folder)
    {
        var location = Classify(folder);

        Assert.Equal(DriveKind.UncShare, location.Kind);
        Assert.True(location.OnNetwork);
    }

    [Fact]
    public void A_mapped_drive_is_a_network_drive()
    {
        Assert.True(Classify(@"H:\Tools\Fabolus").OnNetwork);
    }

    [Fact]
    public void A_folder_inside_a_redirected_downloads_folder_names_it()
    {
        // Folder redirection: Downloads lives on a server, so a folder inside it does too - worth
        // naming, since the user thinks of it as "my Downloads", not as a share.
        var location = Classify(@"\\fileserver\home$\pat\Downloads\Fabolus",
            Known("Desktop", Profile + @"\Desktop"),
            Known("Downloads", @"\\fileserver\home$\pat\Downloads"));

        var match = Assert.Single(location.Under);
        Assert.Equal("Downloads", match.Name);
        Assert.True(match.Redirected);
    }

    [Fact]
    public void A_local_known_folder_is_reported_but_not_as_redirected()
    {
        var location = Classify(Profile + @"\Downloads\Fabolus-1.0.0", Known("Downloads", Profile + @"\Downloads"));

        Assert.False(Assert.Single(location.Under).Redirected);
        Assert.False(location.OnNetwork);
    }

    [Fact]
    public void A_sibling_folder_sharing_a_prefix_is_not_inside()
    {
        var location = Classify(Profile + @"\Downloads2\Fabolus", Known("Downloads", Profile + @"\Downloads"));

        Assert.Empty(location.Under);
    }

    [Fact]
    public void An_installer_folder_counts_as_installed()
    {
        Assert.True(Classify(@"C:\Users\pat\AppData\Local\Programs\Fabolus").Installed);
        Assert.True(Classify(@"c:\program files\Fabolus\").Installed);
    }

    [Theory]
    [InlineData(@"C:\Users\pat\AppData\Local\Temp\Temp1_Fabolus-1.0.0-win-x64-self-contained.zip", "Windows Explorer")]
    [InlineData(@"C:\Users\pat\AppData\Local\Temp\Temp3_Fabolus.zip\Fabolus", "Windows Explorer")]
    [InlineData(@"C:\Users\pat\AppData\Local\Temp\7zO8A3C1B2\Fabolus", "7-Zip")]
    [InlineData(@"C:\Users\pat\AppData\Local\Temp\Rar$EXa1234.5678\Fabolus", "WinRAR")]
    public void A_folder_an_archiver_unpacked_into_temp_is_run_from_inside_a_zip(string folder, string tool)
    {
        var location = Classify(folder);

        Assert.True(location.InTemp);
        Assert.Equal(tool, location.ExtractedBy);
    }

    [Fact]
    public void An_extracted_folder_named_after_its_zip_is_not_mistaken_for_one_run_from_inside()
    {
        // Extract All names the folder after the zip, without the "TempN_" Explorer uses for Run.
        Assert.Null(Classify(Profile + @"\Downloads\Fabolus-1.0.0-win-x64-self-contained").ExtractedBy);
    }
}

public sealed class ModuleClassifierTests
{
    private static ModuleOrigin Classify(string module) => ModuleClassifier.Classify(
        module,
        appFolder: @"\\fileserver\apps\Fabolus",
        windowsFolder: @"C:\Windows",
        programFiles: [@"C:\Program Files", @"C:\Program Files (x86)"],
        programData: @"C:\ProgramData",
        userProfile: @"C:\Users\pat");

    [Theory]
    [InlineData(@"\\fileserver\apps\Fabolus\Fabolus.dll", nameof(ModuleOrigin.App))]
    [InlineData(@"C:\Windows\System32\ntdll.dll", nameof(ModuleOrigin.Windows))]
    [InlineData(@"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.12\coreclr.dll", nameof(ModuleOrigin.DotNet))]
    [InlineData(@"C:\Program Files\dotnet\host\fxr\10.0.12\hostfxr.dll", nameof(ModuleOrigin.DotNet))]
    [InlineData(@"C:\Users\pat\AppData\Local\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.12\wpfgfx_cor3.dll", nameof(ModuleOrigin.DotNet))]
    [InlineData(@"C:\Program Files\CrowdStrike\umppc.dll", nameof(ModuleOrigin.ProgramFiles))]
    [InlineData(@"C:\ProgramData\Vendor\hook64.dll", nameof(ModuleOrigin.ProgramData))]
    [InlineData(@"C:\Users\pat\AppData\Local\Discord\overlay.dll", nameof(ModuleOrigin.UserProfile))]
    [InlineData(@"D:\Elsewhere\thing.dll", nameof(ModuleOrigin.Other))]
    public void Modules_are_sorted_by_where_they_came_from(string module, string expected)
    {
        Assert.Equal(expected, Classify(module).ToString());
    }
}

public sealed class SecurityCenterTests
{
    [Theory]
    [InlineData(0x061100u, "on")]
    [InlineData(0x060100u, "off")]
    [InlineData(0x061110u, "on, definitions out of date")]
    [InlineData(0x062100u, "snoozed")]
    public void Product_state_decodes(uint state, string expected)
    {
        Assert.Equal(expected, SecurityCenter.DescribeState(state));
    }

    [Fact]
    public void Known_agents_are_named_once_whatever_the_case_of_their_process()
    {
        var agents = SecurityCenter.AgentsIn(["explorer", "csfalconservice", "CSFalconService", "MsSense", "notepad"]);

        Assert.Equal(new[] { "CrowdStrike Falcon", "Microsoft Defender for Endpoint" }, agents);
    }
}

public sealed class ZonesTests
{
    [Theory]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://github.com/", 3)]
    [InlineData("[ZoneTransfer]\nReferrerUrl=x\nzoneid = 1\n", 1)]
    [InlineData("[ZoneTransfer]\r\nHostUrl=https://example.com/ZoneId=4", null)]
    [InlineData("", null)]
    public void The_zone_id_is_read_from_its_own_line(string stream, int? expected)
    {
        Assert.Equal(expected, Zones.ParseZoneId(stream));
    }

    [Fact]
    public void Only_zones_other_than_the_local_machine_count_as_downloaded()
    {
        var survey = new FolderSurvey(10, 0, 8,
            new Dictionary<string, int> { [Zones.NotMarked] = 4, [Zones.LocalMachine] = 1, ["Internet"] = 5 },
            ZoneChecked: 10, CloudOnlineOnly: 0, CloudPinned: 0);

        Assert.Equal(5, survey.MarkedAsDownloaded);
    }
}
