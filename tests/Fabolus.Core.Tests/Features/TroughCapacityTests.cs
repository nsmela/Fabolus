using Fabolus.Core.Features.Moulds;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features;

/// <summary>
/// How much the trough - the basin recessed into the top of the mould - holds.
/// </summary>
/// <remarks>
/// The basin is a prism between two parallel planes, so its capacity has a closed form: the area
/// of the rim polygon times the depth. That is what these check against, rather than against a
/// previously recorded number, so a change in how the cutter is built shows up as a disagreement
/// with the geometry instead of as a new baseline to bless.
/// </remarks>
public class TroughCapacityTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    /// <summary>
    /// A 60x60 box. Its mould footprint is the outline grown by the wall thickness, and the rim
    /// is that inset again by the trough margin.
    /// </summary>
    private static IMesh Box() =>
        Engine.Generators.GenerateBox(new Vector3(-30, -30, 0), new Vector3(30, 30, 20)).Value;

    private static ConcaveMouldDefinition Mould(double trough, double margin = 2.5) =>
        new(OffsetXY: 4.0) { TroughHeight = trough, TroughOffset = margin };

    [Fact]
    public void WithNoTrough_TheCapacityIsZero()
    {
        var capacity = Mould(trough: 0).TroughCapacity(Engine, Box());

        capacity.IsSuccess.Should().BeTrue();
        capacity.Value.Should().Be(0.0);
    }

    /// <summary>
    /// A contoured shell follows the bolus surface and has no flat top to recess into, so it
    /// reports nothing however deep the trough is set.
    /// </summary>
    [Fact]
    public void AContouredMouldHasNoTroughToMeasure()
    {
        var capacity = new ContouredMouldDefinition(OffsetXY: 4.0) { TroughHeight = 6.0 }
            .TroughCapacity(Engine, Box());

        capacity.IsSuccess.Should().BeTrue();
        capacity.Value.Should().Be(0.0);
    }

    /// <summary>
    /// A channel trough pools around the air channels, so with no channels there is nothing to
    /// pool around and the mould is built without a basin at all.
    /// </summary>
    [Fact]
    public void AChannelTroughWithNoChannels_MeasuresNothing()
    {
        var capacity = (Mould(trough: 6.0) with { TroughShape = TroughShapeType.Channels })
            .TroughCapacity(Engine, Box());

        capacity.IsSuccess.Should().BeTrue();
        capacity.Value.Should().Be(0.0);
    }

    [Fact]
    public void TheCapacityIsTheRimAreaTimesTheDepth()
    {
        var mould = Mould(trough: 6.0);

        var capacity = mould.TroughCapacity(Engine, Box());
        capacity.IsSuccess.Should().BeTrue();

        // The same rim the cutter is built from: the footprint, inset by the margin.
        var expected = RimArea(mould) * 6.0;
        capacity.Value.Should().BeApproximately(expected, expected * 0.01);
    }

    /// <summary>
    /// Depth only scales the prism, so twice the depth is twice the silicone. This is the
    /// relationship the number is actually used for - dialling a depth in against a target volume.
    /// </summary>
    [Fact]
    public void DoublingTheDepth_DoublesTheCapacity()
    {
        var shallow = Mould(trough: 3.0).TroughCapacity(Engine, Box());
        var deep = Mould(trough: 6.0).TroughCapacity(Engine, Box());

        shallow.IsSuccess.Should().BeTrue();
        deep.IsSuccess.Should().BeTrue();

        deep.Value.Should().BeApproximately(shallow.Value * 2.0, shallow.Value * 0.02);
    }

    /// <summary>
    /// A wider margin leaves a thicker rim and a smaller pool.
    /// </summary>
    [Fact]
    public void AWiderMargin_LeavesLessCapacity()
    {
        var narrow = Mould(trough: 6.0, margin: 2.0).TroughCapacity(Engine, Box());
        var wide = Mould(trough: 6.0, margin: 8.0).TroughCapacity(Engine, Box());

        narrow.IsSuccess.Should().BeTrue();
        wide.IsSuccess.Should().BeTrue();

        wide.Value.Should().BeLessThan(narrow.Value);
    }

    /// <summary>
    /// The area of the rim the basin is cut to, worked out independently of the code under test.
    /// </summary>
    private static double RimArea(MouldDefinition mould)
    {
        var outline = Engine.Polygons.ProjectOutline(Box());
        outline.IsSuccess.Should().BeTrue();

        var footprint = Engine.Polygons.Offset(outline.Value, 4.0);
        footprint.IsSuccess.Should().BeTrue();

        var rim = Engine.Polygons.Offset(footprint.Value, -mould.TroughOffset);
        rim.IsSuccess.Should().BeTrue();

        return rim.Value.Area;
    }
}
