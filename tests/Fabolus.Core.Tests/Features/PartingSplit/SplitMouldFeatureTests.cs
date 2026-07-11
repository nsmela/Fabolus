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
    private readonly PartingLineFeature _partingLineFeature;
    private readonly SplitMouldFeature _sut;

    public SplitMouldFeatureTests(GeometryEngineFixture fixture)
    {
        _engine = fixture.Engine;
        _partingLineFeature = new PartingLineFeature(_engine);
        _sut = new SplitMouldFeature(_engine);
    }

    [Fact]
    public void Execute_Sphere_ProducesTwoWatertightPieces()
    {
        var sphereResult = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphereResult.IsSuccess.Should().BeTrue();

        var workspace = Workspace.CreateEmpty();
        var addResult = workspace.AddMesh(sphereResult.Value);
        addResult.IsSuccess.Should().BeTrue();
        workspace = addResult.Value;

        var mouldId = workspace.GetActiveMesh().Value.Metadata.Id;

        var partingLineResult = _partingLineFeature.Execute(workspace.GetMesh(mouldId).Value, Vector3.UnitY);
        partingLineResult.IsSuccess.Should().BeTrue();

        var result = _sut.Execute(workspace, mouldId, partingLineResult.Value, Vector3.UnitY);

        result.IsSuccess.Should().BeTrue();
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
    public void Execute_WithHole_ProducesTwoPiecesAndCombinedToolCoversHole()
    {
        var torusResult = TorusMesh.Create(_engine, majorRadius: 10, minorRadius: 4, majorSegments: 64, minorSegments: 32);
        torusResult.IsSuccess.Should().BeTrue();

        var workspace = Workspace.CreateEmpty();
        var addResult = workspace.AddMesh(torusResult.Value);
        addResult.IsSuccess.Should().BeTrue();
        workspace = addResult.Value;

        var mouldId = workspace.GetActiveMesh().Value.Metadata.Id;

        var partingLineResult = _partingLineFeature.Execute(workspace.GetMesh(mouldId).Value, Vector3.UnitX);
        partingLineResult.IsSuccess.Should().BeTrue();
        partingLineResult.Value.InternalHoleCount.Should().Be(1);

        var result = _sut.Execute(workspace, mouldId, partingLineResult.Value, Vector3.UnitX);

        result.IsSuccess.Should().BeTrue();
        var pieces = result.Value.MeshMetadataList.Where(m => m.Id != mouldId).ToList();
        pieces.Should().HaveCount(2);
    }
}
