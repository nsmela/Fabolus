using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using FluentAssertions;
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
        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), body);
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

        var pieces = finalWorkspace.Records.Where(r => r.Id != mouldId).ToList();
        pieces.Should().HaveCount(2);

        foreach (var pieceRecord in pieces)
        {
            var pieceMesh = finalWorkspace.GetMesh(pieceRecord.Id).Value;
            var topology = _engine.Evaluators.ValidateTopology(pieceMesh);
            topology.IsSuccess.Should().BeTrue();
            topology.Value.IsWatertight.Should().BeTrue();

            pieceRecord.Commands.OfType<SplitCommand>().Should().ContainSingle(
                "each piece should carry a SplitCommand so it can be reconstructed on import");
        }
    }

    [Fact]
    public void ApplySplit_RecordsTheParametersItRanWith()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        var (workspace, mouldId) = MouldAround(sphere.Value);

        var lineParameters = PartingLineParameters.Default with { PullDirection = Vector3.UnitY };
        var meshParameters = PartingMeshParameters.Default with { Depth = 0.2 };

        var result = new SplitMouldFeature(_engine).Execute(workspace, mouldId, lineParameters, meshParameters);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var commands = result.Value.Records
            .Where(r => r.Id != mouldId)
            .Select(r => r.Commands.OfType<SplitCommand>().Single())
            .ToList();

        // The recipe on each half must be the one that ran, so a replay reproduces this same split.
        commands.Should().AllSatisfy(c =>
        {
            c.LineParameters.PullDirection.Should().Be(Vector3.UnitY);
            c.MeshParameters.Depth.Should().Be(0.2);
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
        result.Value.Records.Where(r => r.Id != mouldId).Should().HaveCount(2);
    }

    /// <summary>
    /// A piece's history replays to the piece. The split command is applied to the mould the replay
    /// rebuilds, and traces its line on the body the replay passed through on the way - which is the
    /// only way it has of reaching the body, since the mould is the mesh in front of it.
    /// </summary>
    [Fact]
    public void ReplayingAPiece_RebuildsIt()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        var (workspace, mouldId) = MouldAround(sphere.Value);

        var result = new SplitMouldFeature(_engine).Execute(
            workspace, mouldId, PartingLineParameters.Default, PartingMeshParameters.Default);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        foreach (var record in result.Value.Records.Where(r => r.Id != mouldId))
        {
            var replayed = CommandReplay.Apply(_engine, record.BaseMesh!, record.Commands);
            replayed.IsSuccess.Should().BeTrue(replayed.IsFailure ? replayed.Error.Description : "");

            var original = _engine.Evaluators.GetStatistics(result.Value.GetMesh(record.Id).Value).Value;
            var rebuilt = _engine.Evaluators.GetStatistics(replayed.Value).Value;
            rebuilt.Volume.Should().BeApproximately(original.Volume, original.Volume * 1e-6,
                $"{record.Name} should come back as the piece it was");
        }
    }

    /// <summary>
    /// Applied on its own, a split has no body to trace on, and says so rather than tracing the mould.
    /// </summary>
    [Fact]
    public void ApplyingASplitWithoutItsHistory_IsRefused()
    {
        IMeshCommand command = new SplitCommand(
            PartingLineParameters.Default, PartingMeshParameters.Default, PartingSide.Positive);

        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32).Value;
        command.Apply(_engine, sphere).Error.Should().Be(MetadataErrors.NeedsEarlierStages);
    }
}
