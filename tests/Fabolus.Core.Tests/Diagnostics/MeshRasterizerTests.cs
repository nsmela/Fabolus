using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Sanity checks on the rasterizer itself. Everything the ridge evaluation concludes is read off
/// these images, so a broken camera basis or a bad fit would be indistinguishable from a broken
/// detector - these pin the renderer down on shapes whose projection is known in closed form.
/// </summary>
[Collection("GeometryEngine collection")]
public class MeshRasterizerTests
{
    private const int Size = 512;
    private const float Radius = 30f;

    private readonly IGeometryEngine _engine;

    public MeshRasterizerTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    private IMesh Sphere() => _engine.Generators.GenerateSphere(Vector3.Zero, Radius, 64).Value;

    [Fact]
    public void Render_Sphere_CoversTheExpectedDiscAndLeavesTheCornersEmpty()
    {
        var sphere = Sphere();
        var camera = Camera.Fit(sphere, Views.Front, Size, Size);
        var image = MeshRasterizer.Render(sphere, camera, Size, Size, RenderOptions.Default);

        float pixelRadius = 0.92f * Size / 2f;
        float expected = MathF.PI * pixelRadius * pixelRadius;
        ((float)image.CountDrawn()).Should().BeApproximately(expected, expected * 0.05f);

        foreach (var (x, y) in new[] { (0, 0), (Size - 1, 0), (0, Size - 1), (Size - 1, Size - 1) })
            float.IsPositiveInfinity(image.Depth[(y * Size) + x])
                .Should().BeTrue("a fitted sphere cannot reach the corners of a square frame");

        float.IsPositiveInfinity(image.Depth[(Size / 2 * Size) + (Size / 2)])
            .Should().BeFalse("the centre of the frame is the nearest point of the sphere");
    }

    /// <summary>
    /// A sphere looks the same from everywhere, so eight views that disagree mean the camera basis or
    /// the fit is wrong - which is exactly the failure that would otherwise be misread as the detector
    /// finding different things from different angles.
    /// </summary>
    [Fact]
    public void Render_Sphere_LooksTheSameFromEveryView()
    {
        var sphere = Sphere();

        var counts = Views.Standard
            .Select(view => MeshRasterizer.Render(
                sphere, Camera.Fit(sphere, view, Size, Size), Size, Size, RenderOptions.Default).CountDrawn())
            .ToList();

        float mean = (float)counts.Average();
        foreach (int count in counts) ((float)count).Should().BeApproximately(mean, mean * 0.02f);
    }

    [Fact]
    public void Render_ShadesTheSphereRatherThanFlatteningIt()
    {
        var sphere = Sphere();
        var camera = Camera.Fit(sphere, Views.Front, Size, Size);
        var image = MeshRasterizer.Render(sphere, camera, Size, Size, RenderOptions.Default);

        var brightness = new List<int>();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (!float.IsPositiveInfinity(image.Depth[(y * Size) + x]))
                    brightness.Add(image.Get(x, y).R);

        (brightness.Max() - brightness.Min()).Should().BeGreaterThan(60,
            "a lit ball has a tonal range; a flat disc is a shading bug");
    }

    /// <summary>
    /// A circle of known radius, drawn round a sphere and viewed down its own axis, must land on a
    /// screen circle of radius <c>r * camera.Scale</c>. That checks the projection, the depth bias and
    /// the line stamping in one shot.
    /// </summary>
    [Fact]
    public void DrawPolyline_PutsTheCurveWhereTheProjectionSaysItShouldBe()
    {
        var sphere = Sphere();
        var camera = Camera.Fit(sphere, Views.Top, Size, Size);
        var image = MeshRasterizer.Render(sphere, camera, Size, Size, RenderOptions.Default);

        // On the sphere's equator, so it is never occluded when viewed down the axis.
        var circle = Enumerable.Range(0, 240)
            .Select(i => 2f * MathF.PI * i / 240f)
            .Select(a => new Vector3(Radius * MathF.Cos(a), 0f, Radius * MathF.Sin(a)))
            .ToList();

        var colour = MeshRasterizer.ContourColour(0);
        MeshRasterizer.DrawPolyline(image, camera, circle, closed: true, colour, RenderOptions.Default);

        var radii = new List<float>();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (image.Get(x, y) == colour)
                    radii.Add(MathF.Sqrt(
                        ((x + 0.5f - (Size / 2f)) * (x + 0.5f - (Size / 2f))) +
                        ((y + 0.5f - (Size / 2f)) * (y + 0.5f - (Size / 2f)))));

        radii.Should().NotBeEmpty("the contour has to be drawn at all");

        float expected = Radius * camera.Scale;
        radii.Average().Should().BeApproximately(expected, expected * 0.02f);
    }

    [Fact]
    public void DrawPolyline_HidesTheRunBehindTheSurfaceWhenOcclusionIsOn()
    {
        var sphere = Sphere();
        var camera = Camera.Fit(sphere, Views.Front, Size, Size);
        var options = RenderOptions.Default with { DrawOccludedLines = false };
        var image = MeshRasterizer.Render(sphere, camera, Size, Size, options);

        // Same equator, now viewed edge-on: the near half is in front of the sphere, the far half behind.
        var circle = Enumerable.Range(0, 240)
            .Select(i => 2f * MathF.PI * i / 240f)
            .Select(a => new Vector3(Radius * MathF.Cos(a), 0f, Radius * MathF.Sin(a)))
            .ToList();

        var colour = MeshRasterizer.ContourColour(0);
        MeshRasterizer.DrawPolyline(image, camera, circle, closed: true, colour, options);

        int drawn = 0;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (image.Get(x, y) == colour) drawn++;

        // The visible run is roughly the near half of a diameter-wide line; the far half must be gone.
        float lineWidth = options.LineWidthPx;
        drawn.Should().BeInRange(
            (int)(Radius * camera.Scale * lineWidth * 0.8f),
            (int)(Radius * camera.Scale * lineWidth * 3.5f));
    }
}
