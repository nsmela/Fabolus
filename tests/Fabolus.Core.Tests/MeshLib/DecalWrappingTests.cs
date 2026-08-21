using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryMeshLib;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class DecalWrappingTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GeometryEngine _engine;

    public DecalWrappingTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = (GeometryEngine)_fixture.Engine;
    }

    [Fact]
    public void BuildTextPrism_OnCylinder_WrapsSmoothlyWithRadialNormals()
    {
        // 1. Generate cylinder along Z axis (radius 15mm, height 60mm)
        var cylinderResult = _fixture.Engine.Generators.GenerateTube(new TubeParameters
        {
            Path = new[] { new Vector3(0, 0, -30), new Vector3(0, 0, 30) },
            Radii = new[] { 15.0f, 15.0f },
            Segments = 64,
            Capped = true
        });

        cylinderResult.IsSuccess.Should().BeTrue();
        var cylinder = cylinderResult.Value;

        // 2. Create a rectangular outline representing a wide label spanning 40mm in X (wrapping ~153 degrees around radius 15mm cylinder)
        var rectOutline = new Polygon2D
        {
            OuterBoundary = new[]
            {
                new Vector2(-20, -3),
                new Vector2(20, -3),
                new Vector2(20, 3),
                new Vector2(-20, 3)
            }
        };

        // Frame anchored at front of cylinder (15, 0, 0) with normal (1, 0, 0) and horizontal tangent along Y (0, 1, 0)
        var frame = DecalFrame.FromHit(new Vector3(15, 0, 0), new Vector3(1, 0, 0), 0f);

        var sw = Stopwatch.StartNew();
        var prismResult = _engine.Generators.BuildTextPrism(
            new[] { rectOutline },
            frame,
            depth: 1.0f,
            sink: -0.2f,
            overshoot: 0.2f,
            maxEdgeLength: 2.0f,
            targetMesh: cylinder);
        sw.Stop();

        prismResult.IsSuccess.Should().BeTrue();
        var prism = prismResult.Value;

        // Verify watertight manifold topology
        var validation = _engine.Evaluators.ValidateTopology(prism).Value;
        validation.IsWatertight.Should().BeTrue();
        validation.IsManifold.Should().BeTrue();
        validation.SelfIntersectionCount.Should().Be(0);

        // Verify vertices wrap around the cylinder:
        // Left edge (-20mm along circumference) should have X < 15, Y < 0
        // Right edge (+20mm along circumference) should have X < 15, Y > 0
        var stats = _engine.Evaluators.GetStatistics(prism).Value;
        stats.MinY.Should().BeLessThan(-10.0);
        stats.MaxY.Should().BeGreaterThan(10.0);
        stats.MinX.Should().BeLessThan(5.0); // Wrapped around the sides

        // Performance check: should complete in under 30ms
        sw.ElapsedMilliseconds.Should().BeLessThan(30);
    }

    [Fact]
    public void BuildTextPrism_OnSphere_GeneratesWatertightMesh()
    {
        // Load or generate sphere
        var sphereResult = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 20.0, 32);
        sphereResult.IsSuccess.Should().BeTrue();
        var sphere = sphereResult.Value;

        var rectOutline = new Polygon2D
        {
            OuterBoundary = new[]
            {
                new Vector2(-15, -3),
                new Vector2(15, -3),
                new Vector2(15, 3),
                new Vector2(-15, 3)
            }
        };

        var frame = DecalFrame.FromHit(new Vector3(0, 0, 20), Vector3.UnitZ, 0f);

        var prismResult = _engine.Generators.BuildTextPrism(
            new[] { rectOutline },
            frame,
            depth: 0.8f,
            sink: -0.2f,
            overshoot: 0.2f,
            maxEdgeLength: 2.0f,
            targetMesh: sphere);

        prismResult.IsSuccess.Should().BeTrue();
        var prism = prismResult.Value;

        var validation = _engine.Evaluators.ValidateTopology(prism).Value;
        validation.IsWatertight.Should().BeTrue();
        validation.IsManifold.Should().BeTrue();
        validation.SelfIntersectionCount.Should().Be(0);
    }

    [Fact]
    public void BuildTextPrism_OnOrganicMould_WrapsSurfaceCleanly()
    {
        var bolus = _fixture.LoadStl("ear_bolus.stl");
        var stats = _engine.Evaluators.GetStatistics(bolus).Value;

        var center = new Vector3(
            (float)(stats.MinX + stats.MaxX) * 0.5f,
            (float)(stats.MinY + stats.MaxY) * 0.5f,
            (float)stats.MaxZ);

        var frame = DecalFrame.FromHit(center, Vector3.UnitZ, 0f);

        var rectOutline = new Polygon2D
        {
            OuterBoundary = new[]
            {
                new Vector2(-12, -2.5f),
                new Vector2(12, -2.5f),
                new Vector2(12, 2.5f),
                new Vector2(-12, 2.5f)
            }
        };

        var prismResult = _engine.Generators.BuildTextPrism(
            new[] { rectOutline },
            frame,
            depth: 0.8f,
            sink: -0.2f,
            overshoot: 0.2f,
            maxEdgeLength: 1.5f,
            targetMesh: bolus);

        prismResult.IsSuccess.Should().BeTrue();
        var prism = prismResult.Value;

        var validation = _engine.Evaluators.ValidateTopology(prism).Value;
        validation.IsWatertight.Should().BeTrue();
        validation.IsManifold.Should().BeTrue();
    }
}
