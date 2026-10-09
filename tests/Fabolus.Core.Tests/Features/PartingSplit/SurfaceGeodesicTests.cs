using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The free geodesic the line editor re-walks a span with when the user turns it on. It used to be
/// MeshLib's; it is GeometryEngine's shortest path now, and these pin that it still answers the way
/// the editor relies on.
/// </summary>
[Collection("GeometryEngine collection")]
public class SurfaceGeodesicTests
{
    private readonly IGeometryEngine _engine;

    public SurfaceGeodesicTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    private static double LengthOf(IReadOnlyList<Vector3> path)
    {
        double length = 0;
        for (int i = 1; i < path.Count; i++) length += path[i - 1].DistanceTo(path[i]);
        return length;
    }

    [Fact]
    public void ThePathFollowsTheSurface_NotTheChordThroughIt()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 20.0, 64).Value;
        using var geodesic = new PartingTools(_engine).CreateSurfaceGeodesic(sphere).Value;

        var path = geodesic.Path(new Vector3(20, 0, 0), new Vector3(0, 20, 0));

        path.Should().NotBeNull();
        LengthOf(path!).Should().BeApproximately(20 * Math.PI / 2, 20 * Math.PI / 2 * 0.01,
            "a quarter of a great circle, give or take the facets");
    }

    [Fact]
    public void BothEndsAreTakenOntoTheSurface()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 20.0, 64).Value;
        var index = _engine.Spatial.IndexFor(sphere).Value;
        using var geodesic = new PartingTools(_engine).CreateSurfaceGeodesic(sphere).Value;

        // Held off the surface, as a dragged handle is.
        var (from, to) = (new Vector3(23, 1, 0), new Vector3(-2, 1, 19));
        var path = geodesic.Path(from, to)!;

        path[0].DistanceTo(index.ClosestPoint(from).Value.Point).Should().BeLessThan(1e-9);
        path[^1].DistanceTo(index.ClosestPoint(to).Value.Point).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void TwoPiecesThatDoNotTouch_HaveNoPath()
    {
        var a = _engine.Generators.GenerateSphere(Vector3.Zero, 5.0, 16).Value;
        var b = _engine.Generators.GenerateSphere(new Vector3(30, 0, 0), 5.0, 16).Value;
        var pair = _engine.CreateMesh(
            a.Vertices.AddRange(b.Vertices),
            a.Triangles.AddRange(b.Triangles.Select(i => i + a.VertexCount)),
            MeshMetadata.Named("pair")).Value;

        using var geodesic = new PartingTools(_engine).CreateSurfaceGeodesic(pair).Value;

        geodesic.Path(new Vector3(5, 0, 0), new Vector3(25, 0, 0)).Should().BeNull();
    }
}
