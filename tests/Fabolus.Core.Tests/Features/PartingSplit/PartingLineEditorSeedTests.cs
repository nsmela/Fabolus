using Fabolus.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// How a traced line is cut into the sections a user takes hold of. A section too short to hold a
/// handle is folded into its neighbour, so two handles never land a millimetre or two apart - a pair a
/// user cannot aim between and the editor refuses to divide.
/// </summary>
public class PartingLineEditorSeedTests
{
    private const int ShortestSection = 6;

    /// <summary>A flat ring of wall: two concentric creases, 10mm and 16mm out.</summary>
    private static PartingBand Ring() => new(Circle(10.0, 400), Circle(16.0, 400));

    private static RidgeContour Circle(double radius, int count)
    {
        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            double t = 2.0 * Math.PI * i / count;
            points.Add(new Vector3(radius * Math.Cos(t), radius * Math.Sin(t), 0.0));
        }

        return new RidgeContour(points, IsClosed: true);
    }

    /// <summary>
    /// A loop centred in the wall except for two stretches that hug the inner crease, with three sound
    /// samples between them at the very start of the analysis: it starts its sections at the first
    /// sound sample, so those three come out as the first section.
    /// </summary>
    private static List<Vector3> LoopWithAShortFirstSection()
    {
        const int count = 120;
        var loop = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            bool hugging = i < 10 || (i >= 13 && i < 21);
            double radius = hugging ? 10.5 : 13.0;
            double t = 2.0 * Math.PI * i / count;
            loop.Add(new Vector3(radius * Math.Cos(t), radius * Math.Sin(t), 0.0));
        }

        return loop;
    }

    [Fact]
    public void AShortFirstSection_IsFoldedIntoTheOneBeforeIt()
    {
        var band = Ring();
        var loop = LoopWithAShortFirstSection();

        // The case itself, checked rather than assumed: without it this test would pass on any seed.
        var report = PartingLineSections.Analyse(loop, band);
        report.Sections[0].Count.Should().BeLessThan(ShortestSection,
            "the loop is built so the analysis opens on a short section");

        var seeded = PartingLineEditor.Seed(loop, band, shortestSection: ShortestSection, longestSection: 0.0);

        seeded.Anchors.Should().NotBeEmpty();
        foreach (var span in seeded.Spans)
            (span.Points.Count - 1).Should().BeGreaterThanOrEqualTo(ShortestSection,
                "no section is shorter than a handle needs");
    }

    [Fact]
    public void FoldingTheFirstSection_KeepsTheWholeLine()
    {
        var band = Ring();
        var loop = LoopWithAShortFirstSection();

        var seeded = PartingLineEditor.Seed(loop, band, shortestSection: ShortestSection, longestSection: 0.0);

        // Every sample still belongs to exactly one section: the spans share their end points, so
        // together they hold one more point per span than the loop has samples.
        seeded.Spans.Sum(s => s.Points.Count).Should().Be(loop.Count + seeded.Spans.Count);
        seeded.Flatten().Should().BeEquivalentTo(loop);
    }
}
