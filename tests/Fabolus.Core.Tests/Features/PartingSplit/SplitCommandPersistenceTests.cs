using System.IO;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A cut/split piece is only usable as a save file if the command that produced it survives a 3MF
/// round trip - otherwise you reload geometry with no history and no way to re-derive it. The save
/// file serializes any <see cref="IMeshCommand"/> generically, so this pins that
/// <see cref="SplitCommand"/> and <see cref="CutCommand"/> actually make it through intact.
/// The commands are recorded by hand on a plain mesh's entry (rather than run through a full mould
/// split) so the test isolates their serialization from the rest of the command chain.
/// </summary>
[Collection("GeometryEngine collection")]
public class SplitCommandPersistenceTests
{
    private readonly IGeometryEngine _engine;

    public SplitCommandPersistenceTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    private IMesh Sphere() => _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32).Value;

    /// <summary>
    /// Saves the mesh under an entry carrying <paramref name="command"/> to a fresh temp .3mf, opens it
    /// again into an empty workspace, and hands the restored entry to the assertion.
    /// </summary>
    private void RoundTrip(IMeshCommand command, Action<MeshRecord> assert)
    {
        var mesh = Sphere();
        var record = MeshRecord.ForImport("sphere").WithBaseMesh(mesh) with { Commands = [command] };

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "roundtrip.3mf");

            var export = new ExportMesh(_engine).Execute(mesh, record, path);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            var import = new ImportMesh(_engine).Execute(Workspace.CreateEmpty(), path);
            import.IsSuccess.Should().BeTrue(import.IsFailure ? import.Error.Description : "");

            assert(import.Value.GetActiveRecord().Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SplitCommand_SurvivesA3mfRoundTrip()
    {
        var command = new SplitCommand(
            PartingLineParameters.Default with { PullDirection = new Vector3(0, 1, 0), NoiseThreshold = 0.2 },
            PartingMeshParameters.Default with { Depth = 0.25, OuterContourMargin = 12.0 },
            PartingSide.Negative);

        RoundTrip(command, imported =>
        {
            var restored = imported.Commands.OfType<SplitCommand>().Should().ContainSingle().Subject;

            restored.Side.Should().Be(PartingSide.Negative);
            restored.LineParameters.PullDirection.Should().Be(new Vector3(0, 1, 0));
            restored.LineParameters.NoiseThreshold.Should().Be(0.2);
            restored.MeshParameters.Depth.Should().Be(0.25);
            restored.MeshParameters.OuterContourMargin.Should().Be(12.0);
        });
    }

    [Fact]
    public void CutCommand_SurvivesA3mfRoundTrip()
    {
        var command = new CutCommand(
            PartingLineParameters.Default with { PullDirection = new Vector3(0, 1, 0) },
            PartingMeshParameters.Default with { Depth = 0.15 },
            PartingResultMode.Separated);

        RoundTrip(command, imported =>
        {
            var restored = imported.Commands.OfType<CutCommand>().Should().ContainSingle().Subject;

            restored.LineParameters.PullDirection.Should().Be(new Vector3(0, 1, 0));
            restored.MeshParameters.Depth.Should().Be(0.15);
            restored.Mode.Should().Be(PartingResultMode.Separated);
        });
    }

    [Fact]
    public void PartingSide_IsSerializedByName_NotIndex()
    {
        var command = new SplitCommand(
            PartingLineParameters.Default,
            PartingMeshParameters.Default,
            PartingSide.Negative);

        // The stored side must be the member name, so reordering the enum can't silently remap an
        // old save file's value.
        var json = MeshCommandSerializer.Serialize([command]);
        json.Should().Contain("Negative");
        json.Should().NotContain("\"Side\":1");
    }
}
