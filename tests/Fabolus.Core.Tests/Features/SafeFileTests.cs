using System.IO;
using Fabolus.Core.Features.MeshIO;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

/// <summary>
/// A project save goes through a temporary file, so one that fails part-way leaves the project
/// that was already on disk as it was rather than truncated.
/// </summary>
public class SafeFileTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Write_ReplacesTheFileAndLeavesNothingElseBehind()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.3mf");
            File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6]);

            var result = SafeFile.WriteAllBytes(path, [9, 8]);

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
            File.ReadAllBytes(path).Should().Equal(9, 8);
            Directory.GetFiles(dir).Should().ContainSingle();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_CreatesAFileThatIsNotThereYet()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.3mf");

            var result = SafeFile.WriteAllBytes(path, [7]);

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
            File.ReadAllBytes(path).Should().Equal(7);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The failure the issue is about: the disk fills, or the device goes away, after some of the
    /// new bytes are out. Writing in place would by then have truncated the old project.
    /// </summary>
    [Fact]
    public void Write_ThatFailsPartWay_LeavesTheOldFileIntactAndNoTemporary()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.3mf");
            byte[] original = [1, 2, 3, 4, 5, 6];
            File.WriteAllBytes(path, original);

            var result = SafeFile.Write(path, stream =>
            {
                stream.Write([9, 9, 9]);
                throw new IOException("There is not enough space on the disk.");
            });

            result.IsFailure.Should().BeTrue();
            result.Error.Description.Should().Contain("not enough space");
            File.ReadAllBytes(path).Should().Equal(original);
            Directory.GetFiles(dir).Should().ContainSingle("the temporary is cleaned up");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_IntoAMissingFolder_FailsWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "bolus.3mf");

        var result = SafeFile.WriteAllBytes(path, [1]);

        result.IsFailure.Should().BeTrue();
    }
}
