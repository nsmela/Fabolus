using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fabolus.StartupCheck.Tests;

/// <summary>On a real folder: the order files are looked at, and the time budget that keeps a network share from stalling the check.</summary>
public sealed class FolderSurveyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fabolus-startupcheck-" + Guid.NewGuid().ToString("N"));

    public FolderSurveyTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "runtimes"));
        File.WriteAllText(Path.Combine(_folder, "b-notes.txt"), "1234");
        File.WriteAllText(Path.Combine(_folder, "Zeta.dll"), "12");
        File.WriteAllText(Path.Combine(_folder, "Fabolus.exe"), "123");
        File.WriteAllText(Path.Combine(_folder, "runtimes", "alpha.dll"), "1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Fabolus_exe_comes_first_then_the_programs_then_everything_else()
    {
        var names = Surveys.ListFiles(_folder).Select(f => f.FullName.Substring(_folder.Length + 1)).ToList();

        Assert.Equal(new[] { "Fabolus.exe", @"runtimes\alpha.dll", "Zeta.dll", "b-notes.txt" }, names);
    }

    [Fact]
    public void Sizes_and_counts_cover_every_file_however_many_marks_were_checked()
    {
        var files = Surveys.ListFiles(_folder);

        var none = Surveys.Folder(files, TimeSpan.FromTicks(-1));
        var all = Surveys.Folder(files, TimeSpan.FromMinutes(1));

        Assert.Equal(0, none.ZoneChecked);
        Assert.Equal(4, all.ZoneChecked);
        Assert.All(new[] { none, all }, survey =>
        {
            Assert.Equal(4, survey.Files);
            Assert.Equal(3, survey.ProgramFiles);
            Assert.Equal(10, survey.Bytes);
        });
        Assert.Equal(new Dictionary<string, int> { [Zones.NotMarked] = 4 }, all.ZoneMarks);
    }

    [Fact]
    public void Opens_are_timed_within_the_budget()
    {
        var files = Surveys.ListFiles(_folder);

        Assert.Equal(0, Surveys.ProbeFileAccess(files, TimeSpan.FromTicks(-1), []).Opened);
        Assert.Equal(3, Surveys.ProbeFileAccess(files, TimeSpan.FromMinutes(1), []).Opened);
    }
}
