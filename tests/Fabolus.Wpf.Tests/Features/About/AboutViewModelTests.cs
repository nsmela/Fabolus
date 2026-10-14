using Fabolus.Wpf.Features.About;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.About;

public class AboutViewModelTests
{
    // The disclaimer is the one piece of the About window with clinical weight, so a build that
    // lost the embedded file or garbled it should fail here rather than show an empty box.
    [Fact]
    public void Disclaimer_LoadsFromTheEmbeddedFile()
    {
        var disclaimer = AboutViewModel.LoadDisclaimer();

        Assert.StartsWith("Fabolus is not a medical device.", disclaimer);
        Assert.Contains("at your own risk", disclaimer);
    }

    // DISCLAIMER.txt is hard-wrapped for the installer; the window wraps to its own width, so
    // only the breaks between paragraphs may survive.
    [Fact]
    public void Disclaimer_JoinsTheHardWrappedLinesOfEachParagraph()
    {
        var paragraphs = AboutViewModel.LoadDisclaimer()
            .Split(Environment.NewLine + Environment.NewLine);

        Assert.True(paragraphs.Length > 1);
        Assert.All(paragraphs, p => Assert.DoesNotMatch("[\r\n]", p));
    }

    [Fact]
    public void Version_IsTheProjectVersionWithoutBuildMetadata()
    {
        var version = new AboutViewModel().Version;

        Assert.Matches(@"^\d+\.\d+\.\d+( \([0-9a-f]{7}\))?$", version);
    }
}
