using System.Linq;
using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Features.Transforms;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class MouldsTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GenerateMould _generateMouldFeature;
    private readonly ClearMould _clearMouldFeature;

    public MouldsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _generateMouldFeature = new GenerateMould(_fixture.Engine);
        _clearMouldFeature = new ClearMould(_fixture.Engine);
    }

    [Fact]
    public void GenerateMould_PreservesCommandsFromSourceMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Rotate(workspace, mesh.Metadata.Id, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;
        var rotatedMeshId = workspace.ActiveMeshId;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);
        var result = _generateMouldFeature.Execute(workspace, rotatedMeshId, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var mouldMesh = result.Value.GetActiveMesh().Value;

        // Boolean ops hand back bare metadata - the source mesh's prior commands (the
        // rotation) must be carried forward explicitly, in addition to the new MouldDefinition.
        mouldMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
        mouldMesh.Metadata.Commands.OfType<MouldDefinition>().Should().HaveCount(1);
    }

    [Fact]
    public void GenerateMould_Contoured_SubtractsTargetMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);

        var result = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        var mouldMesh = updatedWorkspace.GetActiveMesh().Value;

        // Ensure the mould definition is tracked
        mouldMesh.Metadata.MouldDefinition().HasValue.Should().BeTrue();
        mouldMesh.Metadata.MouldDefinition().Value.TargetMeshId.Should().Be(mesh.Metadata.Id);

        // Subtracted target mesh should make it a hollow shell
        var stats = _fixture.Engine.Evaluators.GetStatistics(mouldMesh).Value;
        stats.Volume.Should().BeGreaterThan(0);
        
        // Since it's contoured and subtracted, the volume should be roughly the shell volume
    }

    [Fact]
    public void GenerateMould_Convex_IncludesAirChannels()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var airChannel = new AirChannelModel(
            System.Guid.NewGuid(),
            AirChannelType.Straight,
            2.0, 5.0, 5.0,
            new StraightAirChannel(new Vector3(0, 0, 10), 5.0f, 20.0f, 2.0f, 5.0f)
        );

        var mouldDef = new ConvexMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0)
        {
            AirChannels = new[] { airChannel }
        };

        var result = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var mouldMesh = result.Value.GetActiveMesh().Value;

        mouldMesh.Metadata.MouldDefinition().Value.AirChannels.Count.Should().Be(1);
    }

    [Fact]
    public void GenerateMould_Convex_SubtractsPaintedChannel()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var mouldDef = new ConvexMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var withoutChannel = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);
        withoutChannel.IsSuccess.Should().BeTrue();
        var baseVolume = _fixture.Engine.Evaluators.GetStatistics(withoutChannel.Value.GetActiveMesh().Value).Value.Volume;

        // A stroke painted across the top of the sphere.
        var painted = new PaintedAirChannel(
            new[] { new Vector3(-3, 0, 9.5f), new Vector3(0, 0, 10f), new Vector3(3, 0, 9.5f) },
            Radius: 2.0f,
            TotalLength: 12.0f,
            PenetrationDepth: 1.0f);
        var channel = new AirChannelModel(System.Guid.NewGuid(), AirChannelType.Painted, 2.0, 4.0, 5.0, painted);

        var withChannel = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef with { AirChannels = new[] { channel } });

        withChannel.IsSuccess.Should().BeTrue();
        var channelVolume = _fixture.Engine.Evaluators.GetStatistics(withChannel.Value.GetActiveMesh().Value).Value.Volume;

        // The painted channel carves a void through the mould's top.
        channelVolume.Should().BeLessThan(baseVolume);
    }

    [Fact]
    public void MouldDefinition_JsonRoundTrip_PreservesPaintedChannelPath()
    {
        var path = new List<Vector3> { new(0, 0, 5), new(5, 2, 6), new(10, -1, 5.5f) };
        var channel = new AirChannelModel(
            System.Guid.NewGuid(), AirChannelType.Painted, 2.0, 5.0, 5.0,
            new PaintedAirChannel(path, 2.5f, 12f, 1f));
        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 3.0, OffsetBottom: 4.0, OffsetTop: 5.0)
        {
            AirChannels = new[] { channel }
        };

        // Mirror the exact serializer options GeometryIO uses for 3MF command metadata:
        // export serializes the runtime type as object, import deserializes to the
        // concrete command type by name.
        var json = System.Text.Json.JsonSerializer.Serialize(
            (object)mouldDef,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
        var restored = (ConcaveMouldDefinition?)System.Text.Json.JsonSerializer.Deserialize(
            json, typeof(ConcaveMouldDefinition),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });

        restored.Should().NotBeNull();
        restored!.AirChannels.Should().HaveCount(1);
        var restoredChannel = restored.AirChannels[0];
        restoredChannel.Type.Should().Be(AirChannelType.Painted);

        var restoredPainted = restoredChannel.DomainModel.Should().BeOfType<PaintedAirChannel>().Subject;
        restoredPainted.Path.Should().Equal(path);
        restoredPainted.Radius.Should().Be(2.5f);
        restoredPainted.TotalLength.Should().Be(12f);
        restoredPainted.PenetrationDepth.Should().Be(1f);
    }

    [Fact]
    public void GenerateMould_Concave_GeneratesMould()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);

        var result = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var mouldMesh = result.Value.GetActiveMesh().Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(mouldMesh).Value;
        stats.MaxZ.Should().BeGreaterThan(10);
        stats.MinZ.Should().BeLessThan(-10);
    }

    [Fact]
    public void GenerateMould_EdgeChannel_WidensTheContourToKeepTheWallClosed()
    {
        var workspace = SphereWorkspace(out var meshId);

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 2.0, OffsetBottom: 5.0, OffsetTop: 5.0);

        // Placed right on the sphere's equator, so the channel rises through what would
        // otherwise be the outside face of the mould.
        var channel = new AirChannelModel(
            System.Guid.NewGuid(),
            AirChannelType.Straight,
            2.0, 5.0, 5.0,
            new StraightAirChannel(new Vector3(10, 0, 0), 5.0f, 25.0f, 2.0f, 5.0f));

        var plainStats = GenerateStats(workspace, meshId, mouldDef);
        var channelStats = GenerateStats(workspace, meshId, mouldDef with { AirChannels = new[] { channel } });

        // The contour has to reach the channel's own radius (2.5) plus a full wall (2.0)
        // past its centre, rather than stopping at the bolus wall and letting the channel
        // slice its way out.
        channelStats.MaxX.Should().BeApproximately(14.5, 0.2);
        channelStats.MaxX.Should().BeGreaterThan(plainStats.MaxX + 2.0);
    }

    [Fact]
    public void GenerateMould_EdgeAngledChannel_FollowsTheArcOutwards()
    {
        var workspace = SphereWorkspace(out var meshId);

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 2.0, OffsetBottom: 5.0, OffsetTop: 5.0);

        // An angled channel leaves along the surface normal before arcing back to vertical,
        // so it surfaces further out again than where it was placed.
        var angled = new AngledAirChannel(new Vector3(10, 0, 0), Vector3.UnitX, 3.0f, 25.0f, 2.0f, 2.5f);
        var channel = new AirChannelModel(System.Guid.NewGuid(), AirChannelType.Angled, 2.0, 5.0, 3.0, angled);

        var channelStats = GenerateStats(workspace, meshId, mouldDef with { AirChannels = new[] { channel } });

        // Cone end at x=13, then an arc of radius 2.5 back to vertical, plus the channel
        // radius and the wall on top of that.
        channelStats.MaxX.Should().BeGreaterThan(17.0);
    }

    [Fact]
    public void GenerateMould_CentredChannel_LeavesTheContourAlone()
    {
        var workspace = SphereWorkspace(out var meshId);

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 2.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var channel = new AirChannelModel(
            System.Guid.NewGuid(),
            AirChannelType.Straight,
            2.0, 5.0, 5.0,
            new StraightAirChannel(new Vector3(0, 0, 10), 5.0f, 15.0f, 2.0f, 5.0f));

        var plainStats = GenerateStats(workspace, meshId, mouldDef);
        var channelStats = GenerateStats(workspace, meshId, mouldDef with { AirChannels = new[] { channel } });

        // A channel well inside the bolus outline is already buried; folding it into the
        // contour must not pad the mould out.
        channelStats.MaxX.Should().BeApproximately(plainStats.MaxX, 1e-3);
        channelStats.MaxY.Should().BeApproximately(plainStats.MaxY, 1e-3);
    }

    [Fact]
    public void GenerateMould_Trough_RaisesTheTopAndRecessesABasinIntoIt()
    {
        var workspace = SphereWorkspace(out var meshId);

        var plain = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var troughed = plain with { TroughHeight = 4.0, TroughOffset = 3.0 };

        // The same mould the trough grows into, left solid - the basin has to come out of
        // the height the trough added, not out of the cover over the bolus.
        var solid = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 9.0);

        var troughedStats = GenerateStats(workspace, meshId, troughed);
        var solidStats = GenerateStats(workspace, meshId, solid);

        troughedStats.MaxZ.Should().BeApproximately(solidStats.MaxZ, 1e-3);
        troughedStats.Volume.Should().BeGreaterThan(GenerateStats(workspace, meshId, plain).Volume);
        troughedStats.Volume.Should().BeLessThan(solidStats.Volume);
    }

    [Fact]
    public void GenerateMould_Trough_HollowsOutTheAddedHeightAndSpareTheCover()
    {
        var workspace = SphereWorkspace(out var meshId);

        // Sphere top is z=10, so the cover over it runs 10..15 and the trough's 4mm sits
        // above that, 15..19.
        var troughed = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0)
        {
            TroughHeight = 4.0,
            TroughOffset = 3.0
        };
        var solid = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 9.0);

        var troughedMesh = GenerateMesh(workspace, meshId, troughed);
        var solidMesh = GenerateMesh(workspace, meshId, solid);

        // Above the cover only the rim is left standing.
        var pooled = SliceVolume(troughedMesh, 16.0f, 18.0f);
        pooled.Should().BeLessThan(SliceVolume(solidMesh, 16.0f, 18.0f) * 0.5);

        // The cover itself is untouched - the basin bottoms out on it, it isn't thinned.
        SliceVolume(troughedMesh, 12.0f, 14.0f)
            .Should().BeApproximately(SliceVolume(solidMesh, 12.0f, 14.0f), 1e-3);
    }

    [Fact]
    public void GenerateMould_ChannelTrough_CarvesLessThanAFootprintTrough()
    {
        var workspace = SphereWorkspace(out var meshId);

        var channel = new AirChannelModel(
            System.Guid.NewGuid(),
            AirChannelType.Straight,
            2.0, 5.0, 5.0,
            new StraightAirChannel(new Vector3(0, 0, 10), 5.0f, 20.0f, 2.0f, 5.0f));

        var footprintTrough = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0)
        {
            AirChannels = new[] { channel },
            TroughHeight = 4.0,
            TroughOffset = 3.0
        };
        var channelTrough = footprintTrough with { TroughShape = TroughShapeType.Channels };

        var footprintStats = GenerateStats(workspace, meshId, footprintTrough);
        var channelStats = GenerateStats(workspace, meshId, channelTrough);

        // Both moulds are the same height; the channel trough only pools around the one
        // channel, so it leaves more of the top face standing.
        channelStats.MaxZ.Should().BeApproximately(footprintStats.MaxZ, 1e-3);
        channelStats.Volume.Should().BeGreaterThan(footprintStats.Volume);
    }

    [Fact]
    public void GenerateMould_ChannelTrough_WithoutChannels_LeavesTheMouldUnchanged()
    {
        var workspace = SphereWorkspace(out var meshId);

        var plain = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var troughed = plain with { TroughHeight = 4.0, TroughShape = TroughShapeType.Channels };

        var plainStats = GenerateStats(workspace, meshId, plain);
        var troughedStats = GenerateStats(workspace, meshId, troughed);

        // Nothing to pool around: the mould must not grow taller for a basin that can't be
        // carved.
        troughedStats.MaxZ.Should().BeApproximately(plainStats.MaxZ, 1e-3);
        troughedStats.Volume.Should().BeApproximately(plainStats.Volume, 1e-3);
    }

    [Fact]
    public void MouldDefinition_JsonRoundTrip_PreservesTroughSettings()
    {
        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 3.0, OffsetBottom: 4.0, OffsetTop: 5.0)
        {
            TroughHeight = 6.0,
            TroughOffset = 1.5,
            TroughShape = TroughShapeType.Channels
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            (object)mouldDef,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
        var restored = (ConcaveMouldDefinition?)System.Text.Json.JsonSerializer.Deserialize(
            json, typeof(ConcaveMouldDefinition),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });

        restored.Should().NotBeNull();
        restored!.TroughHeight.Should().Be(6.0);
        restored.TroughOffset.Should().Be(1.5);
        restored.TroughShape.Should().Be(TroughShapeType.Channels);
    }

    [Fact]
    public void MouldDefinition_SavedBeforeTroughsExisted_DeserialisesWithNoTrough()
    {
        // Mould definitions written into 3MFs by earlier builds carry no trough fields.
        var json = """{"OffsetXY":3,"OffsetBottom":4,"OffsetTop":5,"AirChannels":[]}""";

        var restored = (ConcaveMouldDefinition?)System.Text.Json.JsonSerializer.Deserialize(
            json, typeof(ConcaveMouldDefinition),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });

        restored.Should().NotBeNull();
        restored!.TroughHeight.Should().Be(0.0);
    }

    private Workspace SphereWorkspace(out System.Guid meshId)
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        meshId = mesh.Metadata.Id;
        return workspace.AddMesh(mesh).Value.SetActiveMesh(meshId).Value;
    }

    private IMesh GenerateMesh(Workspace workspace, System.Guid meshId, MouldDefinition definition)
    {
        var result = _generateMouldFeature.Execute(workspace, meshId, definition);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : string.Empty);

        return result.Value.GetActiveMesh().Value;
    }

    private MeshStatistics GenerateStats(Workspace workspace, System.Guid meshId, MouldDefinition definition) =>
        _fixture.Engine.Evaluators.GetStatistics(GenerateMesh(workspace, meshId, definition)).Value;

    /// <summary>
    /// How much solid material the mesh has between two heights - enough to tell a basin
    /// recessed into the top from a mould that just grew taller.
    /// </summary>
    private double SliceVolume(IMesh mesh, float zMin, float zMax)
    {
        var slab = new Polygon2D
        {
            OuterBoundary = new[]
            {
                new Vector2(-100, -100), new Vector2(100, -100),
                new Vector2(100, 100), new Vector2(-100, 100)
            }
        };

        var slabMesh = _fixture.Engine.Polygons.ExtrudePolygon(slab, zMin, zMax).Value;
        var sliced = _fixture.Engine.Booleans.Intersect(mesh, slabMesh).Value;

        return _fixture.Engine.Evaluators.GetStatistics(sliced).Value.Volume;
    }

    [Fact]
    public void GenerateMould_DoesNotFork_StaysOnSameMeshEntry()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);
        var result = _generateMouldFeature.Execute(workspace, baseId, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        updatedWorkspace.MeshCount.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().Be(baseId);
        updatedWorkspace.GetActiveMeshMetadata().Value.Id.Should().Be(baseId);
    }

    [Fact]
    public void ClearMould_RemovesMouldButKeepsOtherCommands()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);
        workspace = _generateMouldFeature.Execute(workspace, baseId, mouldDef).Value;

        var result = _clearMouldFeature.Execute(workspace);

        result.IsSuccess.Should().BeTrue();
        var clearedWorkspace = result.Value;

        // Still no fork - clearing stays on the same mesh entry.
        clearedWorkspace.MeshCount.Should().Be(1);
        clearedWorkspace.ActiveMeshId.Should().Be(baseId);

        var clearedMesh = clearedWorkspace.GetActiveMesh().Value;
        clearedMesh.Metadata.MouldDefinition().HasNoValue.Should().BeTrue();
        clearedMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
    }

    [Fact]
    public void Rotate_AfterMould_ClearsGeneratedMould()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);
        workspace = _generateMouldFeature.Execute(workspace, baseId, mouldDef).Value;

        // Rotating again invalidates the mould shell built from the prior rotation.
        workspace = transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var rotatedMesh = workspace.GetActiveMesh().Value;
        rotatedMesh.Metadata.MouldDefinition().HasNoValue.Should().BeTrue();
        rotatedMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
    }

    [Fact]
    public void Rotate_ThenSmooth_ThenMould_KeepsBothSiblingCommands()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        var smoothFeature = new SmoothMesh(_fixture.Engine);

        workspace = transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;
        workspace = smoothFeature.Execute(workspace, new SmoothSettings()).Value;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);
        workspace = _generateMouldFeature.Execute(workspace, workspace.ActiveMeshId, mouldDef).Value;

        // Rotate and Smoothing are siblings (same priority) - generating the mould doesn't
        // clear either of them.
        var mouldMesh = workspace.GetActiveMesh().Value;
        mouldMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
        mouldMesh.Metadata.Commands.OfType<SmoothSettings>().Should().HaveCount(1);
        mouldMesh.Metadata.Commands.OfType<MouldDefinition>().Should().HaveCount(1);
    }
}
