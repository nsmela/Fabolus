using System.IO;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Wpf.Common;
using Moq;

namespace Fabolus.Wpf.Tests.Common;

public sealed class CrashLogTests : IDisposable
{
    private static readonly DateTime At = new(2026, 10, 14, 9, 30, 15, 250);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fabolus-crashlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private CrashLog NewLog() => new(new FileSystem(), _folder, () => At, "1.2.3 (abcdef0)");

    [Fact]
    public void Write_CreatesTheFolderAndNamesTheFileByTime()
    {
        var path = NewLog().Write(new InvalidOperationException("boom"), "test");

        Assert.Equal(Path.Combine(_folder, "crash-20261014-093015-250.txt"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_RecordsVersionContextAndTheWholeExceptionChain()
    {
        var failure = Thrown(() => throw new InvalidOperationException("outer", Thrown(() => throw new ArgumentException("inner cause"))));

        var report = File.ReadAllText(NewLog().Write(failure, "UI thread (recovered)")!);

        Assert.StartsWith("Fabolus 1.2.3 (abcdef0)", report);
        Assert.Contains("Context: UI thread (recovered)", report);
        Assert.Contains("Time:    2026-10-14 09:30:15.250", report);
        Assert.Contains("OS:", report);
        Assert.Contains(".NET:", report);
        Assert.Contains("System.InvalidOperationException: outer", report);
        Assert.Contains("System.ArgumentException: inner cause", report);
        Assert.Contains(nameof(Thrown), report);   // the stack, not just the message
    }

    [Fact]
    public void Write_ListsEveryExceptionAnAggregateWraps()
    {
        var failure = new AggregateException(new InvalidOperationException("first"), new FormatException("second"));

        var report = File.ReadAllText(NewLog().Write(failure, "test")!);

        Assert.Contains("System.InvalidOperationException: first", report);
        Assert.Contains("System.FormatException: second", report);
    }

    // A throw per frame lands many reports within one millisecond; none may overwrite another.
    [Fact]
    public void Write_NumbersReportsThatLandOnTheSameTimestamp()
    {
        var log = NewLog();

        var paths = Enumerable.Range(0, 3).Select(i => log.Write(new Exception($"failure {i}"), "test")).ToList();

        Assert.Equal(
            ["crash-20261014-093015-250.txt", "crash-20261014-093015-250-2.txt", "crash-20261014-093015-250-3.txt"],
            paths.Select(Path.GetFileName));
        Assert.Contains("failure 2", File.ReadAllText(paths[2]!));
    }

    // It runs inside the crash handler, where a second exception would end the app.
    [Fact]
    public void Write_ReturnsNullRatherThanThrowing_WhenTheFolderCannotBeWritten()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>())).Throws(new UnauthorizedAccessException("denied"));

        var log = new CrashLog(fileSystem.Object, _folder, () => At, "1.0.0");

        Assert.Null(log.Write(new Exception("boom"), "test"));
    }

    [Fact]
    public void DefaultFolder_SitsBesideThePreferencesFile()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fabolus", "logs");

        Assert.Equal(expected, CrashLog.DefaultFolder);
    }

    private static Exception Thrown(Action action)
    {
        try { action(); }
        catch (Exception e) { return e; }

        throw new InvalidOperationException("Expected the action to throw.");
    }
}
