using BasicResults;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A slab is a shell whose parting line is known by construction: it runs round the edge, at the
/// mid-plane, and its length is the perimeter. That makes every claim here checkable against the
/// shape rather than against the algorithm's own output.
/// </summary>
[Collection("GeometryEngine collection")]
public class ThicknessPartingTests
{
    private const double Width = 80.0;
    private const double Depth = 50.0;

    private readonly IGeometryEngine _engine;

    public ThicknessPartingTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    [Fact]
    public void Trace_Slab_ReturnsOneClosedLoopRoundTheEdge()
    {
        var (mesh, thickness) = Measured(thickness: 6.0);

        var result = ThicknessParting.Trace(mesh, thickness, ThicknessPartingOptions.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.Loops.Should().HaveCount(1);
        result.Value.InternalHoleCount.Should().Be(0);

        // The perimeter, give or take the relaxation pulling the corners in.
        Perimeter(result.Value.Loops[0])
            .Should().BeApproximately(2.0 * (Width + Depth), 25.0);
    }

    [Fact]
    public void Trace_Slab_PutsTheLineOnTheMidPlane()
    {
        var (mesh, thickness) = Measured(thickness: 6.0);

        var result = ThicknessParting.Trace(mesh, thickness, ThicknessPartingOptions.Default);

        result.IsSuccess.Should().BeTrue();

        // The line falls where the two surfaces are equidistant, which on a slab is halfway up the
        // edge. Anywhere else would mean it had drifted onto one of the faces.
        foreach (var point in result.Value.Loops[0])
            Math.Abs(point.Y).Should().BeLessThan(1.0);
    }

    /// <summary>
    /// The property that motivates the whole approach: the answer comes from the shape, so refining
    /// the mesh must not move it. The silhouette tracer has no such guarantee. Tested on the raw
    /// trace, since the feature-level call resamples afterwards.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void Trace_IsIndependentOfTessellation(int segments)
    {
        var (mesh, thickness) = Measured(thickness: 6.0, segments: segments);

        var result = ThicknessParting.Trace(mesh, thickness, ThicknessPartingOptions.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Loops.Should().HaveCount(1);
        Perimeter(result.Value.Loops[0])
            .Should().BeApproximately(2.0 * (Width + Depth), 25.0, $"at {segments} segments a side");
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(9.0)]
    public void Trace_FindsTheEdgeWhateverTheWallThickness(double thickness)
    {
        var (mesh, measured) = Measured(thickness);

        var result = ThicknessParting.Trace(mesh, measured, ThicknessPartingOptions.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Loops.Should().HaveCount(1);
        foreach (var point in result.Value.Loops[0])
            Math.Abs(point.Y).Should().BeLessThan(thickness / 3.0);
    }

    [Fact]
    public void Trace_Sphere_HasNoBorderToPartAlong()
    {
        // A sphere is not a surface given thickness - every face reads the same diameter, so there
        // is no corridor and no two sides. Saying so is better than inventing a line.
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 20.0, 48);
        var thickness = WallThickness.Measure(_engine, 
            sphere.Value, WallThicknessOptions.Default with { MaxThicknessMm = 60.0 });

        var result = ThicknessParting.Trace(sphere.Value, thickness.Value, ThicknessPartingOptions.Default);

        result.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// Every segment of the line is a step between neighbouring mesh vertices, so a segment far
    /// longer than the rest is a chord the tracing drew across the body - a line visibly off the
    /// surface. This is the check that catches that, whatever caused it.
    /// </summary>
    [Fact]
    public void Trace_StepsAlongTheSurfaceWithoutJumping()
    {
        var (mesh, thickness) = Measured(thickness: 6.0, segments: 12);

        var result = ThicknessParting.Trace(mesh, thickness, ThicknessPartingOptions.Default);

        result.IsSuccess.Should().BeTrue();
        foreach (var loop in result.Value.Loops)
        {
            var segments = new List<double>();
            for (int i = 0; i < loop.Count; i++)
                segments.Add(loop[i].DistanceTo(loop[(i + 1) % loop.Count]));

            segments.Sort();
            double median = segments[segments.Count / 2];
            segments[^1].Should().BeLessThan(median * 5.0, "no segment should be a chord across the body");
        }
    }

    /// <summary>
    /// The line is the boundary of a set of faces, which is a closed cycle only while every edge has
    /// exactly two of them. Where three meet the walk strands mid-loop and closing it anyway draws a
    /// chord clean across the model - so the body is refused instead, and the caller told to repair
    /// it. One bad edge in a few thousand is enough to do it.
    /// </summary>
    [Fact]
    public void Trace_NonManifoldBody_IsRefused()
    {
        var slab = SlabWithNonManifoldFin(thickness: 6.0);
        slab.IsSuccess.Should().BeTrue();
        var thickness = WallThickness.Measure(_engine, slab.Value, WallThicknessOptions.Default);
        thickness.IsSuccess.Should().BeTrue();

        var result = ThicknessParting.Trace(slab.Value, thickness.Value, ThicknessPartingOptions.Default);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("non-manifold");
    }

    [Fact]
    public void Trace_ThicknessFromADifferentMesh_Fails()
    {
        var (small, _) = Measured(thickness: 6.0, segments: 4);
        var (_, fromLarge) = Measured(thickness: 6.0, segments: 16);

        var result = ThicknessParting.Trace(small, fromLarge, ThicknessPartingOptions.Default);

        result.IsFailure.Should().BeTrue("indexing one mesh's faces with another's measurement is nonsense");
    }

    [Fact]
    public void Trace_NullArguments_Fail()
    {
        var (mesh, thickness) = Measured(thickness: 6.0);

        ThicknessParting.Trace(null!, thickness, ThicknessPartingOptions.Default).IsFailure.Should().BeTrue();
        ThicknessParting.Trace(mesh, null!, ThicknessPartingOptions.Default).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// The recipe, not the caller, decides which tracer runs. This is what keeps a saved split
    /// honest: the view and <c>SplitCommand</c>'s replay both come through
    /// <c>GeneratePartingLineFromBody</c>, so if the choice lived at the call site instead, a
    /// reopened project could rebuild itself from a different line than the one approved.
    /// </summary>
    [Fact]
    public void GeneratePartingLineFromBody_FollowsTheSourceOnTheParameters()
    {
        var slab = Slab(thickness: 6.0, segments: 8);
        var body = BodyMesh.Create(slab);
        body.IsSuccess.Should().BeTrue();
        var feature = new PartingMeshFeature(_engine);

        var border = feature.GeneratePartingLineFromBody(body.Value, new PartingLineParameters {
            Source = PartingLineSource.ExtrusionBorder,
            PullDirection = Vector3.UnitY,
        });

        border.IsSuccess.Should().BeTrue();

        // The border ignores the pull direction outright: it is traced from the body's own wall
        // thickness, and the resampling that follows works on arc length, which has no direction in
        // it either. Pulling a different way gives back the same line.
        var sideways = feature.GeneratePartingLineFromBody(body.Value, new PartingLineParameters {
            Source = PartingLineSource.ExtrusionBorder,
            PullDirection = Vector3.UnitX,
        });

        sideways.IsSuccess.Should().BeTrue();
        Perimeter(sideways.Value.Loops[0])
            .Should().BeApproximately(Perimeter(border.Value.Loops[0]), 0.01,
                "the extrusion border does not depend on the pull direction");

    }

    /// <summary>
    /// An unspecified recipe - an older save file, or a caller that never set one - keeps the
    /// behaviour that existed before the border tracer did. The view opens on the border instead and
    /// records that choice, so new work is unaffected by this.
    /// </summary>
    [Fact]
    public void PartingLineParameters_DefaultToTheSilhouette()
    {
        PartingLineParameters.Default.Source.Should().Be(PartingLineSource.Silhouette);
        new PartingLineParameters().Source.Should().Be(PartingLineSource.Silhouette);
    }

    [Fact]
    public void GeneratePartingLineFromThickness_NullBody_Fails()
    {
        var feature = new PartingMeshFeature(_engine);

        feature.GeneratePartingLineFromThickness(null!).IsFailure.Should().BeTrue();
    }

    // --- fixture --- //

    private (IMesh Mesh, WallThickness Thickness) Measured(double thickness, int segments = 8)
    {
        var mesh = Slab(thickness, segments);
        var measured = WallThickness.Measure(_engine, mesh, WallThicknessOptions.Default);
        measured.IsSuccess.Should().BeTrue();
        return (mesh, measured.Value);
    }

    /// <summary>
    /// A slab with one extra triangle hung off an existing edge, so three faces share it. Built by
    /// hand because the engine's own construction repairs this away.
    /// </summary>
    private Result<IMesh> SlabWithNonManifoldFin(double thickness)
    {
        var slab = Slab(thickness, segments: 4);
        var vertices = slab.Vertices.SelectMany(v => new[] { (double)v.X, v.Y, v.Z }).ToList();
        var triangles = slab.Triangles.ToList();

        // A fin standing on the first triangle's first edge: that edge now borders three faces.
        int spike = vertices.Count / 3;
        var a = slab.Vertices[slab.Triangles[0]];
        var b = slab.Vertices[slab.Triangles[1]];
        var apex = ((a + b) / 2.0) + new Vector3(0.0, 0.0, 12.0);
        vertices.AddRange(new[] { (double)apex.X, apex.Y, apex.Z });
        triangles.AddRange(new[] { slab.Triangles[0], slab.Triangles[1], spike });

        return _engine.MeshFrom(vertices.ToArray(), triangles.ToArray());
    }

    private static double Perimeter(IReadOnlyList<Vector3> loop)
    {
        double total = 0.0;
        for (int i = 0; i < loop.Count; i++)
            total += loop[i].DistanceTo(loop[(i + 1) % loop.Count]);
        return total;
    }

    /// <summary>An axis-aligned plate, thickness along Y, every side tessellated into a grid.</summary>
    private IMesh Slab(double thickness, int segments)
    {
        var vertices = new List<double>();
        var triangles = new List<int>();

        var corners = new (double X, double Y, double Z)[8];
        int c = 0;
        foreach (double y in new[] { -thickness / 2.0, thickness / 2.0 })
            foreach (double z in new[] { -Depth / 2.0, Depth / 2.0 })
                foreach (double x in new[] { -Width / 2.0, Width / 2.0 })
                    corners[c++] = (x, y, z);

        AddGrid(corners[0], corners[1], corners[3], corners[2]); // y-
        AddGrid(corners[4], corners[6], corners[7], corners[5]); // y+
        AddGrid(corners[0], corners[4], corners[5], corners[1]); // z-
        AddGrid(corners[2], corners[3], corners[7], corners[6]); // z+
        AddGrid(corners[0], corners[2], corners[6], corners[4]); // x-
        AddGrid(corners[1], corners[5], corners[7], corners[3]); // x+

        var result = _engine.MeshFrom(vertices.ToArray(), triangles.ToArray());
        result.IsSuccess.Should().BeTrue();
        return result.Value;

        void AddGrid((double X, double Y, double Z) p00, (double X, double Y, double Z) p10,
                     (double X, double Y, double Z) p11, (double X, double Y, double Z) p01)
        {
            int baseIndex = vertices.Count / 3;
            for (int i = 0; i <= segments; i++)
                for (int j = 0; j <= segments; j++)
                {
                    double u = (double)i / segments, v = (double)j / segments;
                    vertices.Add(Lerp2(p00.X, p10.X, p01.X, p11.X, u, v));
                    vertices.Add(Lerp2(p00.Y, p10.Y, p01.Y, p11.Y, u, v));
                    vertices.Add(Lerp2(p00.Z, p10.Z, p01.Z, p11.Z, u, v));
                }

            int stride = segments + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = baseIndex + (i * stride) + j;
                    triangles.AddRange(new[] { a, a + stride, a + 1, a + 1, a + stride, a + stride + 1 });
                }
        }

        static double Lerp2(double a00, double a10, double a01, double a11, double u, double v) =>
            (a00 * (1 - u) * (1 - v)) + (a10 * u * (1 - v)) + (a01 * (1 - u) * v) + (a11 * u * v);
    }
}
