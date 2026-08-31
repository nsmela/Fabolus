using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using FluentAssertions;
using System.Numerics;
using Xunit;
using Fabolus.Tests.Fixtures;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Exercises the full pipeline the Parting Split WPF view drives: generate a body, add an air
/// channel, generate a mould around both, then split the mould along a parting line. This
/// stands in for a manual WPF verification pass, since the UI itself can't be driven headlessly.
/// </summary>
[Collection("GeometryEngine collection")]
public class EndToEndSplitTests
{
    private readonly IGeometryEngine _engine;

    public EndToEndSplitTests(GeometryEngineFixture fixture)
    {
        _engine = fixture.Engine;
    }

    [Fact]
    public void Sphere_WithAirChannel_Mould_CanBeSplitAlongPartingLine()
    {
        // 1. Add a sphere as the base body.
        var sphereResult = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphereResult.IsSuccess.Should().BeTrue();

        var workspace = Workspace.CreateEmpty();
        var addResult = workspace.AddMesh(sphereResult.Value);
        addResult.IsSuccess.Should().BeTrue();
        workspace = addResult.Value;

        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;

        // 2. Add a straight air channel poking out of the top of the sphere.
        var channel = new StraightAirChannel(
            StartPoint: new Vector3(0, 0, 10),
            ConeLength: 3f,
            TotalLength: 8f,
            TipDiameter: 2f,
            CylinderDiameter: 4f);

        var channelModel = new AirChannelModel(
            Id: Guid.NewGuid(),
            Type: AirChannelType.Straight,
            TipDiameter: 2,
            ChannelDiameter: 4,
            TipLength: 3,
            DomainModel: channel);

        // 3. Generate a mould that encloses the body and subtracts the air channel.
        var mouldDefinition = new ConvexMouldDefinition(OffsetXY: 3.0, OffsetBottom: 3.0, OffsetTop: 3.0)
        {
            TargetMeshId = bodyId,
            AirChannels = new[] { channelModel }
        };

        var mouldResult = new GenerateMould(_engine).Execute(workspace, bodyId, mouldDefinition);
        mouldResult.IsSuccess.Should().BeTrue($"mould generation failed: {(mouldResult.IsFailure ? mouldResult.Error.Description : string.Empty)}");
        workspace = mouldResult.Value;

        var mouldMesh = workspace.GetMesh(bodyId).Value;
        mouldMesh.Metadata.MouldDefinition().HasValue.Should().BeTrue();

        var mould = MouldMesh.Create(mouldMesh);
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        // 4. Compute the parting line for the mould's body, and confirm it's usable.
        var partingLineResult = new PartingMeshFeature(_engine).GeneratePartingLine(mould.Value, Vector3.UnitY);
        partingLineResult.IsSuccess.Should().BeTrue($"parting line generation failed: {(partingLineResult.IsFailure ? partingLineResult.Error.Description : string.Empty)}");
        partingLineResult.Value.IsValid.Should().BeTrue();

        // 5. Split the mould into two pieces (the two-mesh commit path), from the same parameters the
        //    view would have recorded.
        var splitResult = new SplitMouldFeature(_engine).Execute(
            workspace,
            bodyId,
            PartingLineParameters.Default with {
                Source = PartingLineSource.Silhouette, PullDirection = Vector3.UnitY },
            PartingMeshParameters.Default);
        splitResult.IsSuccess.Should().BeTrue($"split failed: {(splitResult.IsFailure ? splitResult.Error.Description : string.Empty)}");

        var finalWorkspace = splitResult.Value;
        finalWorkspace.MeshCount.Should().Be(3, "the original mould plus the two split pieces should all be present");

        var pieces = finalWorkspace.MeshMetadataList.Where(m => m.Id != bodyId).ToList();
        pieces.Should().HaveCount(2);

        foreach (var pieceMeta in pieces)
        {
            var piece = finalWorkspace.GetMesh(pieceMeta.Id).Value;

            var topology = _engine.Evaluators.ValidateTopology(piece);
            topology.IsSuccess.Should().BeTrue();
            topology.Value.IsWatertight.Should().BeTrue($"{pieceMeta.Name} should be a printable, watertight solid");

            piece.Metadata.Commands.OfType<SplitCommand>().Should().ContainSingle();
            piece.Metadata.Commands.OfType<MouldDefinition>().Should().ContainSingle(
                "the split piece should still carry the mould recipe so it can be fully replayed from the base body on import");
        }
    }
}
