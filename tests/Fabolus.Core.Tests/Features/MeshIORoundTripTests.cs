using System.IO;
using System.IO.Compression;
using System.Linq;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

using Quaternion = System.Numerics.Quaternion;
using SnVector3 = System.Numerics.Vector3;

namespace Fabolus.Tests.Features;

/// <summary>
/// A saved 3MF has to come back as the same workspace entry it left as. Losing the history on the
/// way makes a generated mould reopen as a raw bolus - every offset, channel and decal silently
/// forgotten, and re-saving then writes that loss to disk permanently.
///
/// These go through Fabolus's own Export/Import features rather than the engine's IO directly:
/// the engine carries a string dictionary and knows nothing about commands, so the serialization
/// is Fabolus's to get right and Fabolus's to test.
/// </summary>
[Collection("GeometryEngine collection")]
public class MeshIORoundTripTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly ExportMesh _export;
    private readonly ImportMesh _import;

    public MeshIORoundTripTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _export = new ExportMesh(_fixture.Engine);
        _import = new ImportMesh(_fixture.Engine);
    }

    // Commands must be added in ascending priority order: WithCommand clears anything of a
    // strictly greater priority, so recording a Transform after a Mould would drop the Mould.
    private MeshRecord RoundTrip(params IMeshCommand[] commands) =>
        RoundTrip(out _, commands);

    private MeshRecord RoundTrip(out IMesh importedMesh, params IMeshCommand[] commands)
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh, "scalp");

        var record = workspace.GetRecord(id).Value;
        foreach (var command in commands) record = record.WithCommand(command);

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        try
        {
            var path = Path.Combine(dir.FullName, "round_trip.3mf");
            _export.Execute(mesh, record, path).IsSuccess.Should().BeTrue();

            var imported = _import.Execute(Workspace.CreateEmpty(), path);
            imported.IsSuccess.Should().BeTrue(imported.IsFailure ? imported.Error.Description : string.Empty);

            importedMesh = imported.Value.GetActiveMesh().Value;
            return imported.Value.GetActiveRecord().Value;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void TranslateCommand_RoundTripsItsOffset()
    {
        var restored = RoundTrip(new TranslateCommand(new Vector3(12.5f, -3.25f, 0.75f)))
            .Command<TranslateCommand>();

        restored.Should().NotBeNull();
        restored!.Translation.X.Should().Be(12.5f);
        restored.Translation.Y.Should().Be(-3.25f);
        restored.Translation.Z.Should().Be(0.75f);
    }

    [Fact]
    public void RotateCommand_RoundTripsItsQuaternion()
    {
        // The values from a real saved mould, so the assertion is about float fidelity through
        // JSON rather than about numbers that happen to be exact.
        var rotation = new Quaternion(0.005727913f, 0.52872115f, 0.8487265f, 0.009194699f);

        var restored = RoundTrip(new RotateCommand(rotation)).Command<RotateCommand>();

        restored.Should().NotBeNull();
        restored!.Rotation.X.Should().Be(rotation.X);
        restored.Rotation.Y.Should().Be(rotation.Y);
        restored.Rotation.Z.Should().Be(rotation.Z);
        restored.Rotation.W.Should().Be(rotation.W);
    }

    [Fact]
    public void SmoothSettings_RoundTripsEveryField()
    {
        var restored = RoundTrip(new SmoothSettings(5, 3.5f, 0.2f, 1.5f, 0.5f)).Command<SmoothSettings>();

        restored.Should().NotBeNull();
        restored!.Iterations.Should().Be(5);
        restored.Intensity.Should().Be(3.5f);
        restored.Inflation.Should().Be(0.2f);
        restored.RemeshRatio.Should().Be(1.5f);
        restored.Resolution.Should().Be(0.5f);
    }

    [Fact]
    public void ConcaveMouldDefinition_RoundTripsItsOffsetsAndTrough()
    {
        var targetId = Guid.NewGuid();
        var mould = new ConcaveMouldDefinition(OffsetXY: 2.0, OffsetBottom: 3.0, OffsetTop: 4.0)
        {
            TargetMeshId = targetId,
            TroughHeight = 7.978835978835979,
            TroughOffset = 2.5,
            TroughShape = TroughShapeType.Footprint,
        };

        var restored = RoundTrip(mould).MouldDefinition();

        restored.Should().BeOfType<ConcaveMouldDefinition>();
        var concave = (ConcaveMouldDefinition)restored!;
        concave.OffsetXY.Should().Be(2.0);
        concave.OffsetBottom.Should().Be(3.0);
        concave.OffsetTop.Should().Be(4.0);
        concave.TargetMeshId.Should().Be(targetId);
        concave.TroughHeight.Should().Be(7.978835978835979);
        concave.TroughOffset.Should().Be(2.5);
        concave.TroughShape.Should().Be(TroughShapeType.Footprint);
    }

    [Theory]
    [InlineData(typeof(ConvexMouldDefinition))]
    [InlineData(typeof(ConcaveMouldDefinition))]
    [InlineData(typeof(ContouredMouldDefinition))]
    public void MouldDefinition_ComesBackAsItsOwnSubtype(Type mouldType)
    {
        // MouldDefinition is abstract and the three shapes build completely different geometry.
        // Coming back as the wrong one would rebuild the wrong mould from a correct-looking file.
        MouldDefinition mould = mouldType switch
        {
            _ when mouldType == typeof(ConvexMouldDefinition) => new ConvexMouldDefinition(OffsetXY: 5.0),
            _ when mouldType == typeof(ConcaveMouldDefinition) => new ConcaveMouldDefinition(OffsetXY: 5.0),
            _ => new ContouredMouldDefinition(OffsetXY: 5.0),
        };

        RoundTrip(mould).MouldDefinition().Should().BeOfType(mouldType);
    }

    [Fact]
    public void MouldDefinition_RoundTripsItsAirChannels()
    {
        // IAirChannel is polymorphic, so this is the case a naive serializer loses: the channel
        // survives as a shape with no domain model and the mould regenerates without its vents.
        var angled = new AngledAirChannel(
            new Vector3(30.399727f, 6.2335577f, 59.03179f),
            new Vector3(0.05537729f, -0.31034565f, 0.94900954f),
            TipLength: 3f,
            TotalLength: 12.236076f,
            TipDiameter: 3f,
            Radius: 2.75f,
            PenetrationDepth: 1f);

        var channelId = Guid.NewGuid();
        var mould = new ConcaveMouldDefinition(OffsetXY: 2.0)
        {
            AirChannels = new[]
            {
                new AirChannelModel(channelId, AirChannelType.Angled, 3.0, 5.5, 3.0, angled),
            },
        };

        var restored = RoundTrip(mould).MouldDefinition();

        restored.Should().NotBeNull();
        restored!.AirChannels.Should().HaveCount(1);

        var channel = restored.AirChannels[0];
        channel.Id.Should().Be(channelId);
        channel.Type.Should().Be(AirChannelType.Angled);
        channel.TipDiameter.Should().Be(3.0);
        channel.ChannelDiameter.Should().Be(5.5);
        channel.TipLength.Should().Be(3.0);

        var domain = channel.DomainModel.Should().BeOfType<AngledAirChannel>().Subject;
        domain.StartPoint.X.Should().Be(30.399727f);
        domain.StartPoint.Y.Should().Be(6.2335577f);
        domain.StartPoint.Z.Should().Be(59.03179f);
        domain.TotalLength.Should().Be(12.236076f);
        domain.Radius.Should().Be(2.75f);
        domain.PenetrationDepth.Should().Be(1f);
    }

    private static TextDecal Decal(string text, EmbossTarget target) => new()
    {
        Text = text,
        Operation = EmbossOperation.Engrave,
        Target = target,
        Font = DecalFont.Bold,
        CapHeight = 10f,
        Depth = 0.8f,
        Tracking = 0.4f,
        RotationDeg = 15f,
        Anchor = new Vector3(-5.433342f, -12.982925f, 55.892933f),
        AnchorNormal = new Vector3(0.0022307127f, -0.99999756f, 0f),
        Id = Guid.NewGuid(),
    };

    private static void AssertMatches(TextDecal restored, TextDecal original)
    {
        restored.Text.Should().Be(original.Text);
        restored.Operation.Should().Be(original.Operation);
        restored.Target.Should().Be(original.Target);
        restored.Font.Should().Be(original.Font);
        restored.CapHeight.Should().Be(original.CapHeight);
        restored.Depth.Should().Be(original.Depth);
        restored.Tracking.Should().Be(original.Tracking);
        restored.RotationDeg.Should().Be(original.RotationDeg);
        restored.Anchor.X.Should().Be(original.Anchor.X);
        restored.Anchor.Y.Should().Be(original.Anchor.Y);
        restored.Anchor.Z.Should().Be(original.Anchor.Z);
        restored.AnchorNormal.X.Should().Be(original.AnchorNormal.X);
        restored.AnchorNormal.Y.Should().Be(original.AnchorNormal.Y);
        restored.AnchorNormal.Z.Should().Be(original.AnchorNormal.Z);
        restored.Id.Should().Be(original.Id);
    }

    [Fact]
    public void DecalCommand_RoundTripsEveryDecalField()
    {
        var decal = Decal("scalp", EmbossTarget.Base);

        var restored = RoundTrip(new DecalCommand(new[] { decal })).Command<DecalCommand>();

        restored.Should().NotBeNull();
        restored!.Decals.Should().HaveCount(1);
        AssertMatches(restored.Decals[0], decal);
    }

    [Fact]
    public void MouldDecalCommand_RoundTripsEveryDecalField()
    {
        var first = Decal("scalp", EmbossTarget.Mould);
        var second = Decal("104.5 cc", EmbossTarget.Mould);

        var restored = RoundTrip(new MouldDecalCommand(new[] { first, second })).Command<MouldDecalCommand>();

        restored.Should().NotBeNull();
        restored!.Decals.Should().HaveCount(2);
        AssertMatches(restored.Decals[0], first);
        AssertMatches(restored.Decals[1], second);
    }

    [Fact]
    public void WholeHistory_RoundTripsInOrder()
    {
        // The shape of a real saved mould: rotate, smooth, mould, decal. Order is what replay
        // walks, so a reordered list rebuilds a different mesh from the same commands.
        var restored = RoundTrip(
            new RotateCommand(Quaternion.CreateFromAxisAngle(SnVector3.UnitZ, 0.5f)),
            new SmoothSettings(2),
            new ConcaveMouldDefinition(OffsetXY: 2.0),
            new MouldDecalCommand(new[] { Decal("scalp", EmbossTarget.Mould) }));

        restored.Commands.Select(c => c.GetType()).Should().Equal(
            typeof(RotateCommand),
            typeof(SmoothSettings),
            typeof(ConcaveMouldDefinition),
            typeof(MouldDecalCommand));
    }

    [Fact]
    public void RoundTrip_KeepsTheEntryNameAndGivesItABaseMeshToReplayFrom()
    {
        var restored = RoundTrip(new SmoothSettings(2));

        restored.Name.Should().Be("round_trip");
        restored.BaseMesh.Should().NotBeNull();
    }

    [Fact]
    public void ImportedHistory_IsNotRecentredOnTopOfItself()
    {
        // A mesh arriving with its own history is already in the frame its BaseMesh replays into.
        // Centring it again would move the geometry without moving the BaseMesh, so every
        // replay-from-base view would draw the model offset from what the viewport shows.
        //
        // Built through the real feature rather than by hand, so the geometry and the recorded
        // command genuinely agree the way a saved workspace entry's would.
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh, "scalp");
        workspace = new TransformMesh(_fixture.Engine).Translate(workspace, id, 40f, 0f, 0f).Value;

        var saved = workspace.GetActiveMesh().Value;
        var savedBounds = _fixture.Engine.Evaluators.GetStatistics(saved).Value;

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        try
        {
            var path = Path.Combine(dir.FullName, "translated.3mf");
            _export.Execute(saved, workspace.GetActiveRecord().Value, path).IsSuccess.Should().BeTrue();

            var imported = _import.Execute(Workspace.CreateEmpty(), path);
            imported.IsSuccess.Should().BeTrue();

            var record = imported.Value.GetActiveRecord().Value;

            // One translation, the one that was saved - not a second centring stacked on top.
            record.Commands.OfType<TranslateCommand>().Should().ContainSingle();

            // And the geometry came back where it was, rather than shifted to the origin.
            var reopened = _fixture.Engine.Evaluators.GetStatistics(imported.Value.GetActiveMesh().Value).Value;
            reopened.BoundsMin.X.Should().BeApproximately(savedBounds.BoundsMin.X, 0.01);
            reopened.BoundsMin.Y.Should().BeApproximately(savedBounds.BoundsMin.Y, 0.01);
            reopened.BoundsMin.Z.Should().BeApproximately(savedBounds.BoundsMin.Z, 0.01);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LegacyCommandName_ResolvesThroughTheRegistry()
    {
        // chin_legacy_smooth.3mf was saved when SmoothSettings was called SmoothCommand. A stored
        // name is a persistence contract, so the registry's alias table has to carry it forward.
        var path = _fixture.GetAssetPath("chin_legacy_smooth.3mf");

        var imported = _import.Execute(Workspace.CreateEmpty(), path);

        imported.IsSuccess.Should().BeTrue(imported.IsFailure ? imported.Error.Description : string.Empty);
        imported.Value.GetActiveRecord().Value.Smoothing().Should().NotBeNull();
    }

    [Fact]
    public void UnknownCommandName_FailsRatherThanSilentlyDroppingTheHistory()
    {
        // Dropping an unrecognised command would leave the file's baked geometry disagreeing with
        // the history that claims to describe it - worse than refusing to open it.
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh, "scalp");
        var record = workspace.GetRecord(id).Value.WithCommand(new SmoothSettings(2));

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        try
        {
            var path = Path.Combine(dir.FullName, "tampered.3mf");
            _export.Execute(mesh, record, path).IsSuccess.Should().BeTrue();

            RewriteInPackage(path, "SmoothSettings", "CommandFromTheFuture");

            var imported = _import.Execute(Workspace.CreateEmpty(), path);

            imported.IsFailure.Should().BeTrue();
            imported.Error.Code.Should().Be("Metadata.UnknownCommand");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // Swaps a command name inside the package's model XML, standing in for a file written by a
    // later version of Fabolus than the one reading it.
    private static void RewriteInPackage(string packagePath, string find, string replace)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var entry = archive.Entries.Single(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase));

        string xml;
        using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd();

        xml = xml.Replace(find, replace);

        entry.Delete();
        var replacement = archive.CreateEntry(entry.FullName);
        using var writer = new StreamWriter(replacement.Open());
        writer.Write(xml);
    }
}
