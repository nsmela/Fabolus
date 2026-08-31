using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

[Collection("GeometryEngine collection")]
public class RidgeDetectionTests
{
    private readonly IGeometryEngine _engine;

    public RidgeDetectionTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    [Fact]
    public void FindRidgeFaces_FlatSheet_FindsNothing()
    {
        var sheet = Tent(foldDegrees: 0f, spanMm: 60f, panels: 12);

        var ridges = RidgeDetection.FindRidgeFaces(sheet, RidgeDetectionOptions.Default);

        ridges.Should().NotContain(true, "a plane has no creases to find");
    }

    [Fact]
    public void FindRidgeFaces_Sphere_FindsNothing()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 30.0, 48);
        sphere.IsSuccess.Should().BeTrue();

        var ridges = RidgeDetection.FindRidgeFaces(sphere.Value, RidgeDetectionOptions.Default);

        ridges.Should().NotContain(true, "a sphere curves everywhere and creases nowhere");
    }

    [Fact]
    public void FindRidgeFaces_SharpFold_FindsTheFold()
    {
        var tent = Tent(foldDegrees: 90f, spanMm: 60f, panels: 12);

        var ridges = RidgeDetection.FindRidgeFaces(tent, RidgeDetectionOptions.Default);

        ridges.Should().Contain(true);

        // Both faces either side of the crease, and only those. The panels on each side are flat
        // continuations of the same slope, so nothing beyond the fold itself is marked.
        int marked = ridges.Count(r => r);
        marked.Should().Be(2, "the crease is one edge, shared by one triangle from each side");
    }

    [Fact]
    public void FindRidgeFaces_ShallowFold_FindsNothing()
    {
        // 8 degrees over a 5mm panel is about 0.03/mm - the sort of gentle turn an offset surface
        // makes on its own, well under the grow threshold.
        var tent = Tent(foldDegrees: 8f, spanMm: 60f, panels: 12);

        var ridges = RidgeDetection.FindRidgeFaces(tent, RidgeDetectionOptions.Default);

        ridges.Should().NotContain(true);
    }

    /// <summary>
    /// The claim that makes one set of thresholds usable across models: the same physical crease is
    /// found whether the mesh is coarse or fine, because the measure is curvature rather than the
    /// dihedral angle, which shrinks as triangles do.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(64)]
    public void FindRidgeFaces_SharpFold_IsFoundAtAnyTessellation(int panels)
    {
        var tent = Tent(foldDegrees: 90f, spanMm: 60f, panels: panels);

        var ridges = RidgeDetection.FindRidgeFaces(tent, RidgeDetectionOptions.Default);

        ridges.Should().Contain(true, $"the fold is the same shape at {panels} panels");
        ridges.Count(r => r).Should().Be(2);
    }

    [Fact]
    public void FindRidgeFaces_ShortCrease_IsRejectedAsNoise()
    {
        // A single kinked vertex in an otherwise flat sheet: sharp enough to seed, far too short to
        // be a feature. This is the filter that keeps a stair-stepped CT surface from reading as
        // ridge everywhere.
        var sheet = SheetWithSpike(spanMm: 120f, panels: 24, spikeHeightMm: 4f);

        var ridges = RidgeDetection.FindRidgeFaces(sheet, RidgeDetectionOptions.Default);

        ridges.Should().NotContain(true, "one kinked vertex is noise, not a ridge");
    }

    [Fact]
    public void FindRidgeFaces_NullOrEmptyMesh_ReturnsEmpty()
    {
        RidgeDetection.FindRidgeFaces(null!, RidgeDetectionOptions.Default).Should().BeEmpty();
    }

    [Fact]
    public void Execute_ShadesOnlyByDraft()
    {
        // The rim is drawn as a contour over the top of this shading, not mixed into it, so every
        // face must come back as one of the three draft colours whatever the shape is doing.
        var tent = Tent(foldDegrees: 90f, spanMm: 60f, panels: 12);
        RidgeDetection.FindRidgeFaces(tent, RidgeDetectionOptions.Default).Should().Contain(true);

        var result = new ComputePartingDirectionColors()
            .Execute(tent, new PartingLineParameters { PullDirection = Vector3.UnitY });

        result.IsSuccess.Should().BeTrue();
        for (int t = 0; t < result.Value.Length / 3; t++)
        {
            var rgb = (result.Value[t * 3], result.Value[(t * 3) + 1], result.Value[(t * 3) + 2]);
            rgb.Should().BeOneOf((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.8, 0.8, 0.8));
        }
    }

    [Fact]
    public void Execute_Sphere_SplitsEvenlyByDraft()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 30.0, 48);
        var sut = new ComputePartingDirectionColors();

        var result = sut.Execute(
            sphere.Value, new PartingLineParameters { PullDirection = Vector3.UnitY });

        result.IsSuccess.Should().BeTrue();

        int red = 0, green = 0;
        int faces = result.Value.Length / 3;
        for (int t = 0; t < faces; t++)
        {
            if (result.Value[t * 3] > 0.9) red++;
            if (result.Value[(t * 3) + 1] > 0.9) green++;
        }

        red.Should().BeGreaterThan(faces / 3);
        green.Should().BeGreaterThan(faces / 3);
    }

    // --- helpers --- //

    /// <summary>
    /// A rectangular sheet of <paramref name="panels"/> quads running along X, folded by
    /// <paramref name="foldDegrees"/> about the Z axis at its midpoint. Panel width shrinks as
    /// <paramref name="panels"/> rises while the fold itself stays put, which is exactly the
    /// tessellation-versus-shape distinction the detector is meant to be insensitive to.
    /// </summary>
    private IMesh Tent(float foldDegrees, float spanMm, int panels)
    {
        if (panels % 2 != 0) throw new ArgumentException("needs a panel boundary at the midpoint", nameof(panels));

        float half = spanMm / 2f;
        float step = spanMm / panels;
        float slope = MathF.Tan(foldDegrees * MathF.PI / 360f); // half the fold each side

        var vertices = new List<double>();
        for (int i = 0; i <= panels; i++)
        {
            float x = -half + (i * step);
            float y = (half - MathF.Abs(x)) * slope;
            foreach (float z in new[] { -half, half })
            {
                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);
            }
        }

        var triangles = new List<int>();
        for (int i = 0; i < panels; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            triangles.AddRange(new[] { a, b, c, b, d, c });
        }

        var result = _engine.CreateMesh(vertices.ToArray(), triangles.ToArray());
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>A flat sheet with one interior vertex pulled out of plane - an isolated kink.</summary>
    private IMesh SheetWithSpike(float spanMm, int panels, float spikeHeightMm)
    {
        float half = spanMm / 2f;
        float step = spanMm / panels;

        var vertices = new List<double>();
        for (int i = 0; i <= panels; i++)
            for (int j = 0; j <= panels; j++)
            {
                vertices.Add(-half + (i * step));
                vertices.Add(i == panels / 2 && j == panels / 2 ? spikeHeightMm : 0f);
                vertices.Add(-half + (j * step));
            }

        int stride = panels + 1;
        var triangles = new List<int>();
        for (int i = 0; i < panels; i++)
            for (int j = 0; j < panels; j++)
            {
                int a = (i * stride) + j, b = a + 1, c = a + stride, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }

        var result = _engine.CreateMesh(vertices.ToArray(), triangles.ToArray());
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }
}
