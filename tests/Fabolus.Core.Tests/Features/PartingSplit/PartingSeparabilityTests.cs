using System.Collections.Immutable;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Pins the two separability measurements on shapes built to have a known answer: blocks that part
/// and blocks that hook, a flat sheet and a sheet folded back over itself.
/// </summary>
[Collection("GeometryEngine collection")]
public class PartingSeparabilityTests
{
    private readonly IGeometryEngine _engine;

    public PartingSeparabilityTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    // ---------------------------------------------------------------- the finished halves

    [Fact]
    public void TwoStackedBlocks_PullApart()
    {
        var lower = Box(0, 0, 0, 10, 5, 10);
        var upper = Box(0, 5.1, 0, 10, 10, 10);

        var pulled = PartingSeparability.OfHalves(_engine, [upper, lower], Vector3.UnitY);

        pulled.IsSuccess.Should().BeTrue(pulled.IsFailure ? pulled.Error.Description : "");
        pulled.Value.Parts.Should().BeTrue();
        pulled.Value.WorstOverlapMm3.Should().Be(0.0);
        pulled.Value.Along.Should().Be(1.0, "the upper block leaves upward; downward it is driven into the lower one");
    }

    /// <summary>
    /// A block held in a clamp: over it, down one side and under it. Whichever way the block is slid
    /// along the axis it meets a jaw, which is what a flange that loops back does to a half.
    /// </summary>
    [Fact]
    public void ABlockHeldTopAndBottom_DoesNotPullApart()
    {
        var block = Box(0, 0, 0, 10, 5, 10);
        var clamp = _engine.Booleans.Union(
        [
            Box(0, 5.1, 0, 12, 10, 10),      // over
            Box(10.1, -3, 0, 12, 10, 10),    // down the side
            Box(0, -3, 0, 12, -0.1, 10),     // under
        ]).Value;

        var pulled = PartingSeparability.OfHalves(_engine, [block, clamp], Vector3.UnitY);

        pulled.IsSuccess.Should().BeTrue(pulled.IsFailure ? pulled.Error.Description : "");
        pulled.Value.Parts.Should().BeFalse();
        pulled.Value.WorstOverlapMm3.Should().BeGreaterThan(100.0);
        pulled.Value.Overlap.Should().NotBeNull("the overlap is what shows the user where the halves lock");
    }

    [Fact]
    public void OnePiece_HasNothingToPullApart() =>
        PartingSeparability.OfHalves(_engine, [Box(0, 0, 0, 1, 1, 1)], Vector3.UnitY)
            .IsFailure.Should().BeTrue();

    // ---------------------------------------------------------------- the flange

    [Fact]
    public void AFlatSheet_IsSingleValued()
    {
        var flange = Ribbon([(-25, 0), (25, 0)]);

        var measured = PartingSeparability.OfFlange(_engine, flange, Body(), Mould(), Vector3.UnitY).Value;

        measured.IsSingleValued.Should().BeTrue();
        measured.TurnedOverMm2.Should().Be(0.0);
        measured.DoubledOverMm2.Should().Be(0.0);

        // The sheet inside the mould, less the part inside the body: 40 x 40 less 10 x 10. Loosely,
        // because a face counts by where its middle falls.
        measured.MatingAreaMm2.Should().BeInRange(1350.0, 1650.0);
    }

    /// <summary>
    /// A sheet that goes out level, climbs back over itself and goes on: the profile of an undercut.
    /// The returning stretch faces the other way from the rest, and beneath it the flange lies three
    /// deep.
    /// </summary>
    [Fact]
    public void ASheetFoldedBackOverItself_IsNot()
    {
        var flange = Ribbon([(-25, 0), (15, 0), (12, 2), (25, 2)]);

        var measured = PartingSeparability.OfFlange(_engine, flange, Body(), Mould(), Vector3.UnitY).Value;

        measured.IsSingleValued.Should().BeFalse();

        // The returning stretch is sqrt(13) long and 40 deep inside the mould; the doubled strip under
        // it is 3 wide and 40 deep, give or take a cell of the half-millimetre grid at each edge.
        measured.TurnedOverMm2.Should().BeInRange(120.0, 170.0);
        measured.DoubledOverMm2.Should().BeInRange(100.0, 145.0);
        measured.Suspect.Should().Contain(true);
    }

    /// <summary>
    /// A patch laid beside the flange and wound the other way is not a fold. Read against one sign for
    /// the whole mesh it would all be "turned over" while lying perfectly flat, which is what a hole
    /// patch on a body with a hole through it looks like.
    /// </summary>
    [Fact]
    public void ASecondSheetWoundTheOtherWay_IsNotAFold()
    {
        var one = Ribbon([(6, 0), (12, 0)]);
        var other = Ribbon([(18, 0), (13, 0)]);
        var both = _engine.CreateMesh(
            one.Vertices.AddRange(other.Vertices),
            one.Triangles.AddRange(other.Triangles.Select(corner => corner + one.VertexCount)),
            MeshMetadata.Named("two sheets")).Value;

        var measured = PartingSeparability.OfFlange(_engine, both, Body(), Mould(), Vector3.UnitY).Value;

        measured.TurnedOverMm2.Should().Be(0.0);
        measured.IsSingleValued.Should().BeTrue();
    }

    // ---------------------------------------------------------------- fixtures

    private IMesh Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        _engine.Generators.GenerateBox(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)).Value;

    private IMesh Body() => Box(-5, -5, -5, 5, 5, 5);

    private IMesh Mould() => Box(-20, -20, -20, 20, 20, 20);

    /// <summary>
    /// A sheet with the given profile in X and Y, carried along Z from -25 to 25 in millimetre steps
    /// and cut every millimetre along the profile too, so each face is small enough to fall on one side
    /// of the body or the mould.
    /// </summary>
    private IMesh Ribbon((double X, double Y)[] profile)
    {
        var points = new List<(double X, double Y)>();
        for (int i = 0; i + 1 < profile.Length; i++)
        {
            var (ax, ay) = profile[i];
            var (bx, by) = profile[i + 1];
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay)))));
            for (int s = 0; s < steps; s++)
                points.Add((ax + ((bx - ax) * s / steps), ay + ((by - ay) * s / steps)));
        }
        points.Add(profile[^1]);

        const int rows = 51;
        var vertices = ImmutableArray.CreateBuilder<Vector3>(points.Count * rows);
        foreach (var (x, y) in points)
            for (int r = 0; r < rows; r++)
                vertices.Add(new Vector3(x, y, -25.0 + r));

        var triangles = ImmutableArray.CreateBuilder<int>();
        for (int p = 0; p + 1 < points.Count; p++)
            for (int r = 0; r + 1 < rows; r++)
            {
                int a = (p * rows) + r, b = ((p + 1) * rows) + r;
                triangles.AddRange(a, b, b + 1);
                triangles.AddRange(a, b + 1, a + 1);
            }

        return _engine.CreateMesh(vertices.ToImmutable(), triangles.ToImmutable(), MeshMetadata.Named("ribbon")).Value;
    }
}
