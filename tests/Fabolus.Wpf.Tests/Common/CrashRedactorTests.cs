using Fabolus.Wpf.Common;

namespace Fabolus.Wpf.Tests.Common;

public class CrashRedactorTests
{
    // The shapes .NET's own file exceptions take, plus the unquoted forms a library might use.
    [Theory]
    [InlineData(@"Could not find file 'C:\Scans\Jane Doe 2026-10-01.stl'.", "Could not find file '<path>'.")]
    [InlineData(@"Could not find a part of the path 'D:/scans/Jane Doe/'.", "Could not find a part of the path '<path>'.")]
    [InlineData(@"Access to the path '\\clinic-nas\scans\Jane Doe\bolus.3mf' is denied.", "Access to the path '<path>' is denied.")]
    [InlineData(@"The process cannot access the file 'c:\users\me\Jane.obj' because it is being used by another process.",
        "The process cannot access the file '<path>' because it is being used by another process.")]
    [InlineData(@"Reading C:\Scans\Jane.stl", "Reading <path>")]
    [InlineData(@"Opening file:///C:/Scans/Jane.stl", "Opening <path>")]
    [InlineData("Could not parse \"Jane Doe.ply\".", "Could not parse \"<path>\".")]
    [InlineData("Bad header in Jane_Doe.STL at byte 80", "Bad header in <path> at byte 80")]
    [InlineData("Profile 'Jane settings.json' is invalid", "Profile '<path>' is invalid")]
    public void Redact_RemovesPathsAndFileNames(string message, string expected)
    {
        Assert.Equal(expected, new CrashRedactor().Redact(message));
    }

    // What makes a crash log worth reading has to survive.
    [Theory]
    [InlineData("Object reference not set to an instance of an object.")]
    [InlineData("Index was outside the bounds of the array.")]
    [InlineData("Specified argument was out of the range of valid values. (Parameter 'index')\nActual value was 42.")]
    [InlineData("Boolean operation failed with status: NotManifold")]
    [InlineData("See https://github.com/nsmela/Fabolus/issues for help.")]
    [InlineData("Took 12:30.5 s; ratio 3:1")]
    public void Redact_LeavesOrdinaryMessagesAlone(string message)
    {
        Assert.Equal(message, new CrashRedactor().Redact(message));
    }

    [Fact]
    public void Redact_RemovesMeshNames_WhereverTheyAppear()
    {
        var redactor = new CrashRedactor();
        redactor.AddSensitiveNames(["Jane Doe"]);

        Assert.Equal(
            "Mesh '<name> (positive)' is not a valid manifold; <name> has 12 holes",
            redactor.Redact("Mesh 'Jane Doe (positive)' is not a valid manifold; JANE DOE has 12 holes"));
    }

    // A mesh deleted just before the failure may still be named in it.
    [Fact]
    public void AddSensitiveNames_KeepsEarlierNames()
    {
        var redactor = new CrashRedactor();
        redactor.AddSensitiveNames(["Jane Doe"]);
        redactor.AddSensitiveNames(["John Roe"]);

        Assert.Equal("<name> and <name>", redactor.Redact("Jane Doe and John Roe"));
    }

    // Longest first, so a name containing another is not left half done.
    [Fact]
    public void AddSensitiveNames_RedactsTheLongerOfTwoOverlappingNamesWhole()
    {
        var redactor = new CrashRedactor();
        redactor.AddSensitiveNames(["Doe", "Jane Doe"]);

        Assert.Equal("<name> was cut", redactor.Redact("Jane Doe was cut"));
    }

    [Fact]
    public void AddSensitiveNames_IgnoresNamesTooShortToMean()
    {
        var redactor = new CrashRedactor();
        redactor.AddSensitiveNames(["ab", " ", ""]);

        Assert.Equal("a table about absent tabs", redactor.Redact("a table about absent tabs"));
    }

    [Fact]
    public void Redact_ReturnsEmptyForNoMessage()
    {
        Assert.Equal(string.Empty, new CrashRedactor().Redact(null));
    }
}
