using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.MeshLib;

/// <summary>
/// The measurement is checkable against shapes whose thickness is known by construction, which is
/// the point of it: a slab is its own answer, and a sphere's is its diameter.
/// </summary>
[Collection("GeometryEngine collection")]
public class WallThicknessTests
{
    private readonly IGeometryEngine _engine;

    public WallThicknessTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    [Theory]
    [InlineData(4f)]
    [InlineData(6f)]
    [InlineData(9f)]
    public void MeasureWallThickness_Slab_ReturnsItsThickness(float thickness)
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: thickness);

        var result = _engine.Evaluators.MeasureWallThickness(slab, WallThicknessOptions.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Median.Should().BeApproximately(thickness, 0.2f);
    }

    [Fact]
    public void MeasureWallThickness_Sphere_ReturnsItsDiameter()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 20.0, 48);
        sphere.IsSuccess.Should().BeTrue();

        var result = _engine.Evaluators.MeasureWallThickness(
            sphere.Value, WallThicknessOptions.Default with { MaxThicknessMm = 60f });

        result.IsSuccess.Should().BeTrue();
        result.Value.Median.Should().BeApproximately(40f, 0.5f, "every inward normal is a diameter");
    }

    /// <summary>
    /// The measurement is of the shape, not of the mesh, so refining the mesh must not move it.
    /// That independence is the reason it is worth having: curvature-based measures on these bodies
    /// swing by a factor of two with the triangle size.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(20)]
    public void MeasureWallThickness_IsIndependentOfTessellation(int segments)
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f, segments: segments);

        var result = _engine.Evaluators.MeasureWallThickness(slab, WallThicknessOptions.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Median.Should().BeApproximately(6f, 0.2f, $"at {segments} segments a side");
    }

    /// <summary>
    /// The distinction the measurement exists to make. A face on the plate's broad side looks
    /// straight across the wall and reads its thickness; a face on the edge looks lengthwise down
    /// the plate and reads nothing, because past the search limit the probe is no longer crossing a
    /// wall. That is precisely how a rim tells itself apart from the surfaces it joins.
    /// </summary>
    [Fact]
    public void MeasureWallThickness_TellsBroadFacesFromEdgeFaces()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(
            slab, WallThicknessOptions.Default with { MaxThicknessMm = 20f });

        result.IsSuccess.Should().BeTrue();

        var (broad, edge) = SplitByOrientation(slab, result.Value);

        broad.Should().NotBeEmpty();
        broad.Should().OnlyContain(t => MathF.Abs(t - 6f) < 0.2f, "a broad face looks across the wall");

        edge.Should().NotBeEmpty();
        edge.Should().OnlyContain(t => float.IsPositiveInfinity(t), "an edge face looks along the plate");
    }

    /// <summary>Splits the per-face measurements by whether the face normal runs along Y (the thickness).</summary>
    private static (List<float> Broad, List<float> Edge) SplitByOrientation(IMesh mesh, WallThickness thickness)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        var broad = new List<float>();
        var edge = new List<float>();

        for (int f = 0; f < thickness.PerFace.Count; f++)
        {
            var a = vertices[triangles[f * 3]];
            var b = vertices[triangles[(f * 3) + 1]];
            var c = vertices[triangles[(f * 3) + 2]];

            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (MathF.Abs(normal.Y) > 0.9f) broad.Add(thickness.PerFace[f]);
            else edge.Add(thickness.PerFace[f]);
        }

        return (broad, edge);
    }

    [Fact]
    public void MeasureWallThickness_SearchTooShort_MeasuresNothing()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(
            slab, WallThicknessOptions.Default with { MaxThicknessMm = 2f });

        result.IsSuccess.Should().BeTrue();
        result.Value.Statistics.UnmeasuredFraction.Should().Be(1f);
        result.Value.Median.Should().Be(0f, "nothing was measured, so there is no median to report");
        result.Value.Statistics.MeasuredFaces.Should().Be(0);
    }

    [Fact]
    public void MeasureWallThickness_Statistics_DescribeAnEvenWall()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(slab, WallThicknessOptions.Default);

        result.IsSuccess.Should().BeTrue();
        var stats = result.Value.Statistics;

        stats.Median.Should().BeApproximately(6f, 0.2f);
        stats.Mean.Should().BeApproximately(6f, 0.2f);
        stats.Minimum.Should().BeApproximately(6f, 0.2f);
        stats.Maximum.Should().BeApproximately(6f, 0.2f);
        stats.FifthPercentile.Should().BeApproximately(6f, 0.2f);
        stats.NinetyFifthPercentile.Should().BeApproximately(6f, 0.2f);

        // A slab is the same thickness everywhere, so the spread is the search tolerance and no more.
        stats.StandardDeviation.Should().BeLessThan(0.2f);
        stats.MeasuredFaces.Should().BeLessThan(stats.TotalFaces, "the edge faces never exit");
        stats.TotalFaces.Should().Be(result.Value.PerFace.Count);
    }

    [Fact]
    public void MeasureWallThickness_Statistics_ExcludeFacesThatNeverExited()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(
            slab, WallThicknessOptions.Default with { MaxThicknessMm = 20f });

        result.IsSuccess.Should().BeTrue();

        // Were the unmeasured faces folded in as "very thick" they would drag the maximum to the
        // search limit and the mean well past the real wall.
        result.Value.Statistics.Maximum.Should().BeApproximately(6f, 0.2f);
        result.Value.Statistics.UnmeasuredFraction.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void MeasureWallThickness_PerVertex_MatchesPerFaceOnAnEvenWall()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(slab, WallThicknessOptions.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PerVertex.Should().HaveCount(slab.Vertices.Length);

        var measured = result.Value.PerVertex.Where(v => float.IsFinite(v)).ToList();
        measured.Should().NotBeEmpty();
        measured.Should().OnlyContain(v => MathF.Abs(v - 6f) < 0.2f);
    }

    [Fact]
    public void MeasureWallThickness_PerVertex_IsInfiniteWhereNoFaceCouldBeMeasured()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(
            slab, WallThicknessOptions.Default with { MaxThicknessMm = 2f });

        result.IsSuccess.Should().BeTrue();
        result.Value.PerVertex.Should().OnlyContain(v => float.IsPositiveInfinity(v),
            "no face exited, so no vertex has anything to average");
    }

    [Fact]
    public void MeasureWallThickness_CarriesTheOptionsThatProducedIt()
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);
        var options = WallThicknessOptions.Default with { MaxThicknessMm = 18f, ToleranceMm = 0.05f };

        var result = _engine.Evaluators.MeasureWallThickness(slab, options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Options.Should().Be(options,
            "what counts as unmeasured depends entirely on how far the search looked");
    }

    [Fact]
    public void MeasureWallThickness_NullMesh_Fails()
    {
        _engine.Evaluators.MeasureWallThickness(null!, WallThicknessOptions.Default)
            .IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(0f, 24, 0.1f)]
    [InlineData(25f, 0, 0.1f)]
    [InlineData(25f, 24, 0f)]
    public void MeasureWallThickness_NonsenseOptions_Fail(float maxMm, int steps, float tolerance)
    {
        var slab = Slab(width: 80f, depth: 50f, thickness: 6f);

        var result = _engine.Evaluators.MeasureWallThickness(slab, new WallThicknessOptions {
            MaxThicknessMm = maxMm,
            CoarseSteps = steps,
            ToleranceMm = tolerance,
        });

        result.IsFailure.Should().BeTrue();
    }

    // --- fixture --- //

    /// <summary>An axis-aligned rectangular plate, thickness along Y, tessellated into a grid.</summary>
    private IMesh Slab(float width, float depth, float thickness, int segments = 6)
    {
        var vertices = new List<double>();
        var triangles = new List<int>();

        var corners = new (float X, float Y, float Z)[8];
        int c = 0;
        foreach (float y in new[] { -thickness / 2f, thickness / 2f })
            foreach (float z in new[] { -depth / 2f, depth / 2f })
                foreach (float x in new[] { -width / 2f, width / 2f })
                    corners[c++] = (x, y, z);

        // Each face of the box as its own grid, so the slab is closed and every quad is subdivided.
        AddGrid(corners[0], corners[1], corners[3], corners[2]); // bottom, y-
        AddGrid(corners[4], corners[6], corners[7], corners[5]); // top, y+
        AddGrid(corners[0], corners[4], corners[5], corners[1]); // z-
        AddGrid(corners[2], corners[3], corners[7], corners[6]); // z+
        AddGrid(corners[0], corners[2], corners[6], corners[4]); // x-
        AddGrid(corners[1], corners[5], corners[7], corners[3]); // x+

        var result = _engine.CreateMesh(vertices.ToArray(), triangles.ToArray());
        result.IsSuccess.Should().BeTrue();
        return result.Value;

        void AddGrid((float X, float Y, float Z) p00, (float X, float Y, float Z) p10,
                     (float X, float Y, float Z) p11, (float X, float Y, float Z) p01)
        {
            int baseIndex = vertices.Count / 3;
            for (int i = 0; i <= segments; i++)
                for (int j = 0; j <= segments; j++)
                {
                    float u = (float)i / segments, v = (float)j / segments;
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

        static double Lerp2(float a00, float a10, float a01, float a11, float u, float v) =>
            (a00 * (1 - u) * (1 - v)) + (a10 * u * (1 - v)) + (a01 * (1 - u) * v) + (a11 * u * v);
    }
}
