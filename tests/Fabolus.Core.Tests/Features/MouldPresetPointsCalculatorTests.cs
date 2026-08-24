using System;
using System.Numerics;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class MouldPresetPointsCalculatorTests
{
    private readonly GeometryEngineFixture _fixture;

    public MouldPresetPointsCalculatorTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Calculate_OnConvexMouldMesh_ReturnsSixPresetPointsWithOutwardNormals()
    {
        var sphereResult = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 10), 10, 24);
        sphereResult.IsSuccess.Should().BeTrue();
        var sphere = sphereResult.Value;

        var mouldDef = new ConvexMouldDefinition(OffsetXY: 5, OffsetBottom: 5, OffsetTop: 5);
        var mouldResult = mouldDef.Generate(_fixture.Engine, sphere);
        mouldResult.IsSuccess.Should().BeTrue();
        var mouldMesh = mouldResult.Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(mouldMesh).Value;
        float zMid = (float)(stats.MinZ + stats.MaxZ) * 0.5f;

        var presets = MouldPresetPointsCalculator.Calculate(_fixture.Engine, mouldMesh);

        presets.Should().HaveCount(6);

        // Front (Y near MinY, Z near zMid, Normal pointing -Y)
        var front = presets.Should().ContainSingle(p => p.Name == "Front").Subject;
        front.Position.X.Should().BeApproximately(0f, 1.0f);
        front.Position.Y.Should().BeApproximately((float)stats.MinY, 1.0f);
        front.Position.Z.Should().BeApproximately(zMid, 0.5f);
        front.Normal.Y.Should().BeLessThan(-0.8f);

        // Back (Y near MaxY, Z near zMid, Normal pointing +Y)
        var back = presets.Should().ContainSingle(p => p.Name == "Back").Subject;
        back.Position.X.Should().BeApproximately(0f, 1.0f);
        back.Position.Y.Should().BeApproximately((float)stats.MaxY, 1.0f);
        back.Position.Z.Should().BeApproximately(zMid, 0.5f);
        back.Normal.Y.Should().BeGreaterThan(0.8f);

        // Left (X near MinX, Z near zMid, Normal pointing -X)
        var left = presets.Should().ContainSingle(p => p.Name == "Left").Subject;
        left.Position.X.Should().BeApproximately((float)stats.MinX, 1.0f);
        left.Position.Y.Should().BeApproximately(0f, 1.0f);
        left.Position.Z.Should().BeApproximately(zMid, 0.5f);
        left.Normal.X.Should().BeLessThan(-0.8f);

        // Right (X near MaxX, Z near zMid, Normal pointing +X)
        var right = presets.Should().ContainSingle(p => p.Name == "Right").Subject;
        right.Position.X.Should().BeApproximately((float)stats.MaxX, 1.0f);
        right.Position.Y.Should().BeApproximately(0f, 1.0f);
        right.Position.Z.Should().BeApproximately(zMid, 0.5f);
        right.Normal.X.Should().BeGreaterThan(0.8f);

        // Curve 1 and Curve 2
        var curve1 = presets.Should().ContainSingle(p => p.Name == "Curve 1").Subject;
        var curve2 = presets.Should().ContainSingle(p => p.Name == "Curve 2").Subject;
        curve1.Position.Z.Should().BeApproximately(zMid, 0.5f);
        curve2.Position.Z.Should().BeApproximately(zMid, 0.5f);

        // Rotation & AvailableSpan
        front.RotationDeg.Should().Be(0f);
        front.AvailableSpan.Should().BeApproximately((float)(stats.MaxX - stats.MinX), 1e-2f);

        back.RotationDeg.Should().Be(0f);
        back.AvailableSpan.Should().BeApproximately((float)(stats.MaxX - stats.MinX), 1e-2f);

        left.RotationDeg.Should().Be(90f);
        left.AvailableSpan.Should().BeApproximately((float)(stats.MaxZ - stats.MinZ), 1e-2f);

        right.RotationDeg.Should().Be(90f);
        right.AvailableSpan.Should().BeApproximately((float)(stats.MaxZ - stats.MinZ), 1e-2f);

        curve1.RotationDeg.Should().Be(90f);
        curve2.RotationDeg.Should().Be(90f);
    }

    [Theory]
    [InlineData(50f, 7, 5.0f)]
    [InlineData(50f, 1, 10.0f)]
    [InlineData(50f, 25, 3.0f)]
    [InlineData(30f, 5, 4.2f)]
    public void CalculateSuggestedCapHeight_ReturnsBoundedValues(float mouldHeight, int charCount, float expectedCapHeight)
    {
        float capHeight = MouldPresetPointsCalculator.CalculateSuggestedCapHeight(mouldHeight, charCount);
        capHeight.Should().BeApproximately(expectedCapHeight, 0.1f);
    }
}
