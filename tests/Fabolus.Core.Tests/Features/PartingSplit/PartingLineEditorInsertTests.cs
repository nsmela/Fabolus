using System.Numerics;
using Fabolus.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A placement outlives the line it was planned against: the view works one out when the cursor moves
/// and applies it when the button goes down, and an edit in between renumbers the sections under it.
/// These cover what <see cref="PartingLineEditor.Insert(SectionedPartingLine, PartingInsertion)"/> does
/// with one that no longer names anything, which used to be to index Spans straight out of range.
/// </summary>
public class PartingLineEditorInsertTests
{
    /// <summary>Two handles, each span a straight run of five samples along X.</summary>
    private static SectionedPartingLine TwoSpanLine()
    {
        var anchors = new[]
        {
            new PartingAnchor(new Vector3(0, 0, 0), PartingAnchorOrigin.Section),
            new PartingAnchor(new Vector3(4, 0, 0), PartingAnchorOrigin.Section),
        };

        var out_ = new List<Vector3>();
        for (int i = 0; i <= 4; i++) out_.Add(new Vector3(i, 0, 0));

        var back = new List<Vector3>();
        for (int i = 4; i >= 0; i--) back.Add(new Vector3(i, 0, 1));
        back[0] = new Vector3(4, 0, 0);
        back[^1] = new Vector3(0, 0, 0);

        return new SectionedPartingLine(
            anchors,
            new[]
            {
                new PartingSpan(out_, PartingLineCondition.Sound, IsRetraced: false),
                new PartingSpan(back, PartingLineCondition.Sound, IsRetraced: false),
            });
    }

    [Fact]
    public void APlacementNamingASectionThatIsGoneDividesNothing()
    {
        var line = TwoSpanLine();

        // Planned against a line that had more sections than this one now does - what a Remove between
        // the plan and the click leaves behind.
        var stale = new PartingInsertion(Rim: 0, Span: 7, Point: 2, At: new Vector3(2, 0, 0));

        var (result, anchor) = PartingLineEditor.Insert(line, stale);

        anchor.Should().Be(-1, "a placement that names no section cannot divide one");
        result.Should().BeSameAs(line, "and the line must come back untouched rather than half-edited");
    }

    [Fact]
    public void APlacementPastTheEndOfItsSectionDividesNothing()
    {
        var line = TwoSpanLine();

        // The section is still there, but a retrace has since left it with fewer samples than the plan
        // counted on.
        var stale = new PartingInsertion(Rim: 0, Span: 0, Point: 40, At: new Vector3(2, 0, 0));

        var (result, anchor) = PartingLineEditor.Insert(line, stale);

        anchor.Should().Be(-1);
        result.Should().BeSameAs(line);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void APlacementOnAHandleDividesNothing(int point)
    {
        // Splitting at either end of a section leaves a span with no length and two handles on top of
        // each other - the same refusal TryPlace makes when it plans the placement.
        var line = TwoSpanLine();

        var (result, anchor) = PartingLineEditor.Insert(
            line, new PartingInsertion(Rim: 0, Span: 0, Point: point, At: new Vector3(0, 0, 0)));

        anchor.Should().Be(-1);
        result.Should().BeSameAs(line);
    }

    [Fact]
    public void APlacementInsideASectionStillDividesIt()
    {
        var line = TwoSpanLine();

        var (result, anchor) = PartingLineEditor.Insert(
            line, new PartingInsertion(Rim: 0, Span: 0, Point: 2, At: new Vector3(2, 0, 0)));

        anchor.Should().Be(1);
        result.Anchors.Should().HaveCount(3);
        result.Spans.Should().HaveCount(3, "the divided section became two, and the other is untouched");

        result.Anchors[1].Position.Should().Be(new Vector3(2, 0, 0));
        result.Anchors[1].IsUserPlaced.Should().BeTrue();

        // The halves share the sample the handle sits on, which is what keeps Flatten's drop-the-last
        // -point rule from losing it.
        result.Spans[0].Points[^1].Should().Be(result.Spans[1].Points[0]);
    }
}
