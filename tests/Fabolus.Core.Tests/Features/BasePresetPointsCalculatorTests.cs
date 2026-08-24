using System;
using System.Numerics;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class BasePresetPointsCalculatorTests
{
    private readonly GeometryEngineFixture _fixture;

    public BasePresetPointsCalculatorTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Calculate_OnSphereMesh_ReturnsThreeHorizontalPresetPoints()
    {
        var sphereResult = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 10), 10, 24);
        sphereResult.IsSuccess.Should().BeTrue();
        var sphere = sphereResult.Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(sphere).Value;
        float zMid = (float)(stats.MinZ + stats.MaxZ) * 0.5f;

        var presets = BasePresetPointsCalculator.Calculate(_fixture.Engine, sphere);

        presets.Should().HaveCount(3);

        // Top (Z near MaxZ, Normal pointing +Z)
        var top = presets.Should().ContainSingle(p => p.Name == "Top").Subject;
        top.Position.X.Should().BeApproximately(0f, 1.0f);
        top.Position.Y.Should().BeApproximately(0f, 1.0f);
        top.Position.Z.Should().BeApproximately((float)stats.MaxZ, 1.0f);
        top.Normal.Z.Should().BeGreaterThan(0.8f);
        top.RotationDeg.Should().Be(0f);
        top.Target.Should().Be(EmbossTarget.Base);

        // Front (Y near MinY, Z near zMid, Normal pointing -Y)
        var front = presets.Should().ContainSingle(p => p.Name == "Front").Subject;
        front.Position.X.Should().BeApproximately(0f, 1.0f);
        front.Position.Y.Should().BeApproximately((float)stats.MinY, 1.0f);
        front.Position.Z.Should().BeApproximately(zMid, 1.0f);
        front.Normal.Y.Should().BeLessThan(-0.8f);
        front.RotationDeg.Should().Be(0f);
        front.Target.Should().Be(EmbossTarget.Base);

        // Back (Y near MaxY, Z near zMid, Normal pointing +Y)
        var back = presets.Should().ContainSingle(p => p.Name == "Back").Subject;
        back.Position.X.Should().BeApproximately(0f, 1.0f);
        back.Position.Y.Should().BeApproximately((float)stats.MaxY, 1.0f);
        back.Position.Z.Should().BeApproximately(zMid, 1.0f);
        back.Normal.Y.Should().BeGreaterThan(0.8f);
        back.RotationDeg.Should().Be(0f);
        back.Target.Should().Be(EmbossTarget.Base);
    }
}
