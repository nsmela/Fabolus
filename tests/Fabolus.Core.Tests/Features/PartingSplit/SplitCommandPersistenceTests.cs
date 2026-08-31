using System.IO;
using System.IO.Compression;
using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A cut/split piece is only usable as a save file if the command that produced it survives a 3MF
/// round trip - otherwise you reload geometry with no history and no way to re-derive it. The 3MF
/// I/O serializes any <see cref="Fabolus.Core.Geometry.Metadata.IMeshCommand"/> generically, so this
/// pins that <see cref="SplitCommand"/> and <see cref="CutCommand"/> actually make it through intact.
/// The commands are attached by hand to a plain mesh (rather than run through a full mould split) so
/// the test isolates their serialization from the rest of the command chain.
/// </summary>
[Collection("GeometryEngine collection")]
public class SplitCommandPersistenceTests
{
    private readonly IGeometryEngine _engine;

    public SplitCommandPersistenceTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    private IMesh Sphere() => _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32).Value;

    /// <summary>Exports the mesh to a fresh temp .3mf, re-imports it, and hands both to the assertion.</summary>
    private void RoundTrip(IMesh mesh, Action<IMesh, string> assert)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "roundtrip.3mf");

            var export = _engine.IO.Export(mesh, path);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            var import = _engine.IO.Import(path);
            import.IsSuccess.Should().BeTrue(import.IsFailure ? import.Error.Description : "");

            assert(import.Value, path);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static string ReadModelXml(string threeMfPath)
    {
        using var archive = ZipFile.OpenRead(threeMfPath);
        using var stream = archive.GetEntry("3D/3dmodel.model")!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void SplitCommand_SurvivesA3mfRoundTrip()
    {
        var command = new SplitCommand(
            PartingLineParameters.Default with { PullDirection = new Vector3(0, 1, 0), NoiseThreshold = 0.2f },
            PartingMeshParameters.Default with { Depth = 0.25f, OuterContourMargin = 12f },
            PartingSide.Negative);

        var mesh = Sphere();
        mesh = mesh.WithMetadata(mesh.Metadata.WithCommand(command));

        RoundTrip(mesh, (imported, _) =>
        {
            var restored = imported.Metadata.Commands.OfType<SplitCommand>().Should().ContainSingle().Subject;

            restored.Side.Should().Be(PartingSide.Negative);
            restored.LineParameters.PullDirection.Should().Be(new Vector3(0, 1, 0));
            restored.LineParameters.NoiseThreshold.Should().Be(0.2f);
            restored.MeshParameters.Depth.Should().Be(0.25f);
            restored.MeshParameters.OuterContourMargin.Should().Be(12f);
        });
    }

    [Fact]
    public void CutCommand_SurvivesA3mfRoundTrip()
    {
        var command = new CutCommand(
            PartingLineParameters.Default with { PullDirection = new Vector3(0, 1, 0) },
            PartingMeshParameters.Default with { Depth = 0.15f },
            PartingResultMode.Separated);

        var mesh = Sphere();
        mesh = mesh.WithMetadata(mesh.Metadata.WithCommand(command));

        RoundTrip(mesh, (imported, _) =>
        {
            var restored = imported.Metadata.Commands.OfType<CutCommand>().Should().ContainSingle().Subject;

            restored.LineParameters.PullDirection.Should().Be(new Vector3(0, 1, 0));
            restored.MeshParameters.Depth.Should().Be(0.15f);
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

        var mesh = Sphere();
        mesh = mesh.WithMetadata(mesh.Metadata.WithCommand(command));

        RoundTrip(mesh, (_, path) =>
        {
            // The stored side must be the member name, so reordering the enum can't silently
            // remap an old save file's value.
            var xml = ReadModelXml(path);
            xml.Should().Contain("Negative");
            xml.Should().NotContain("\"Side\":1");
        });
    }
}
