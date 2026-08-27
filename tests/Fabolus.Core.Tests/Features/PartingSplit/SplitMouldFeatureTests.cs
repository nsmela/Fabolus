using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using FluentAssertions;
using System.Numerics;
using Xunit;
using Fabolus.Tests.Fixtures;

namespace Fabolus.Core.Tests.Features.PartingSplit;

[Collection("GeometryEngine collection")]
public class SplitMouldFeatureTests
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;

    public SplitMouldFeatureTests(GeometryEngineFixture fixture)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
    }

    /// <summary>
    /// Wraps a body in a convex mould and returns the workspace plus the mould's id. The split needs
    /// a real mould, not a bare solid: the parting mesh spans outward from the parting line and
    /// relies on the cavity to reach the middle, so a solid body keeps an uncut central column.
    /// </summary>
    private (Workspace Workspace, Guid MouldId) MouldAround(IMesh body)
    {
        var workspace = Workspace.CreateEmpty();
        var added = workspace.AddMesh(body);
        added.IsSuccess.Should().BeTrue();
        workspace = added.Value;

        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;
        var definition = new ConvexMouldDefinition(OffsetXY: 3.0, OffsetBottom: 3.0, OffsetTop: 3.0)
        {
            TargetMeshId = bodyId
        };

        var mould = new GenerateMould(_engine).Execute(workspace, bodyId, definition);
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        return (mould.Value, bodyId);
    }

    [Fact]
    public void ApplySplit_Sphere_ProducesTwoWatertightPieces()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphere.IsSuccess.Should().BeTrue();

        var (workspace, mouldId) = MouldAround(sphere.Value);

        var result = new SplitMouldFeature(_engine).Execute(
            workspace, mouldId, PartingLineParameters.Default, PartingMeshParameters.Default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
        var finalWorkspace = result.Value;

        // Original mould plus the two new pieces.
        finalWorkspace.MeshCount.Should().Be(3);

        var pieces = finalWorkspace.MeshMetadataList.Where(m => m.Id != mouldId).ToList();
        pieces.Should().HaveCount(2);

        foreach (var pieceMeta in pieces)
        {
            var pieceMesh = finalWorkspace.GetMesh(pieceMeta.Id).Value;
            var topology = _engine.Evaluators.ValidateTopology(pieceMesh);
            topology.IsSuccess.Should().BeTrue();
            topology.Value.IsWatertight.Should().BeTrue();

            pieceMesh.Metadata.Commands.OfType<SplitCommand>().Should().ContainSingle(
                "each piece should carry a SplitCommand so it can be reconstructed on import");
        }
    }

    [Fact]
    public void ApplySplit_RecordsTheParametersItRanWith()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        var (workspace, mouldId) = MouldAround(sphere.Value);

        var lineParameters = PartingLineParameters.Default with { PullDirection = Vector3.UnitY };
        var meshParameters = PartingMeshParameters.Default with { Depth = 0.2f };

        var result = new SplitMouldFeature(_engine).Execute(workspace, mouldId, lineParameters, meshParameters);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var commands = result.Value.MeshMetadataList
            .Where(m => m.Id != mouldId)
            .Select(m => result.Value.GetMesh(m.Id).Value.Metadata.Commands.OfType<SplitCommand>().Single())
            .ToList();

        // The recipe on each half must be the one that ran, so a replay reproduces this same split.
        commands.Should().AllSatisfy(c =>
        {
            c.LineParameters.PullDirection.Should().Be(Vector3.UnitY);
            c.MeshParameters.Depth.Should().Be(0.2f);
        });

        commands.Select(c => c.Side).Should().BeEquivalentTo(
            new[] { PartingSide.Positive, PartingSide.Negative },
            "the two halves should record opposite sides of the same split");
    }

    [Fact]
    public void ApplySplit_WithHole_ProducesTwoPieces()
    {
        // Hole along +Y so it lines up with the pull direction and the flange axis.
        var torus = TorusMesh.Create(
            _engine, majorRadius: 10, minorRadius: 4, majorSegments: 64, minorSegments: 32, holeAxis: Vector3.UnitY);
        torus.IsSuccess.Should().BeTrue();

        var (workspace, mouldId) = MouldAround(torus.Value);

        var result = new SplitMouldFeature(_engine).Execute(
            workspace, mouldId, PartingLineParameters.Default, PartingMeshParameters.Default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
        result.Value.MeshMetadataList.Where(m => m.Id != mouldId).Should().HaveCount(2);
    }
}
