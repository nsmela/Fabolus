using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Which corners the parting code treats as the same point. A triangle soup has to weld whole, or it
/// has no adjacency at all; a surface that already closes up by index must not, or a smoothed body
/// that touches itself comes back with edges carrying four faces and the trace refuses it.
/// </summary>
[Collection("GeometryEngine collection")]
public class SeamWeldTests
{
    private readonly IGeometryEngine _engine;

    public SeamWeldTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    private IMesh Box(Vector3 min, Vector3 max) => _engine.Generators.GenerateBox(min, max).Value;

    /// <summary>
    /// Two boxes touching along one edge, each closed on its own - how GeometryEngine's offset hands
    /// back a surface that touches itself: the point once per sheet, at the same position.
    /// </summary>
    private IMesh BoxesTouchingAlongAnEdge()
    {
        var first = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var second = Box(new Vector3(1, 1, 0), new Vector3(2, 2, 1));
        return _engine.CreateMesh(
            first.Vertices.AddRange(second.Vertices),
            first.Triangles.AddRange(second.Triangles.Select(i => i + first.Vertices.Length)),
            first.Metadata).Value;
    }

    [Fact]
    public void ATriangleSoup_WeldsWhole()
    {
        var box = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var soup = _engine.CreateMesh(
            [.. box.Triangles.Select(i => box.Vertices[i])],
            [.. Enumerable.Range(0, box.Triangles.Length)],
            box.Metadata).Value;

        SeamWeld.Weld(soup, 0.001, out int points);

        points.Should().Be(8);
    }

    [Fact]
    public void ASurfaceTouchingItself_KeepsBothCopiesOfThePoint()
    {
        SeamWeld.Weld(BoxesTouchingAlongAnEdge(), 0.001, out int points);

        points.Should().Be(16, "each box keeps its own corners on the edge they share");
    }

    [Fact]
    public void ASurfaceTouchingItself_MeasuresAsTwoClosedPieces()
    {
        // Welded by position, the shared edge carried four faces and the Euler characteristic came
        // out 3 - neither one sphere nor two.
        var topology = RidgeDetection.MeasureTopology(BoxesTouchingAlongAnEdge());

        topology.IsClosed.Should().BeTrue();
        topology.EulerCharacteristic.Should().Be(4);
    }
}
