using System.IO;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A parting result is a single mesh; the separated/combined choice only decides how export writes it.
/// These pin that: a separated mesh-format export writes one file per half (name_A, name_B), a combined
/// one writes a single file, and a 3MF export is always one project file regardless of the mode.
/// </summary>
[Collection("GeometryEngine collection")]
public class ExportPartingTests
{
    private readonly IGeometryEngine _engine;

    public ExportPartingTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    /// <summary>Builds a convex mould around a sphere and applies a parting with the given mode,
    /// returning the single parted mesh and its entry.</summary>
    private (IMesh Mesh, MeshRecord Record) PartedMould(PartingResultMode mode)
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphere.IsSuccess.Should().BeTrue();

        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), sphere.Value);

        var mould = new GenerateMould(_engine).Execute(workspace, bodyId,
            new ConvexMouldDefinition(OffsetXY: 3.0, OffsetBottom: 3.0, OffsetTop: 3.0) { TargetMeshId = bodyId });
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");
        workspace = mould.Value;

        var applied = new SplitMouldFeature(_engine).ExecuteCut(
            workspace, bodyId,
            PartingLineParameters.Default with { Source = PartingLineSource.Silhouette },
            PartingMeshParameters.Default, mode);
        applied.IsSuccess.Should().BeTrue(applied.IsFailure ? applied.Error.Description : "");

        var piece = applied.Value.Records.Single(r => r.Id != bodyId);
        return (applied.Value.GetMesh(piece.Id).Value, piece);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Separated_MeshFormat_WritesOneFilePerHalf()
    {
        var (mesh, record) = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var export = new ExportMesh(_engine).Execute(mesh, record, path, overwrite: true);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            File.Exists(Path.Combine(dir, "bolus_A.stl")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "bolus_B.stl")).Should().BeTrue();
            File.Exists(path).Should().BeFalse("a separated export writes per-half files, not the base name");
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The save dialog confirms replacing the name the user picked - "bolus.stl" - but a separated
    /// export writes "bolus_A.stl" and "bolus_B.stl", which the dialog never saw. Those are only
    /// replaced once the caller has asked about them by name.
    /// </summary>
    [Fact]
    public void Separated_MeshFormat_DoesNotReplaceExistingPiecesUnasked()
    {
        var (mesh, record) = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var pieceA = Path.Combine(dir, "bolus_A.stl");
            var pieceB = Path.Combine(dir, "bolus_B.stl");
            File.WriteAllText(pieceA, "earlier export");

            var export = new ExportMesh(_engine).Execute(mesh, record, path, overwrite: true);

            export.IsFailure.Should().BeTrue();
            export.Error.Code.Should().Be(MeshErrors.ExportFilesExist([pieceA]).Code);
            File.ReadAllText(pieceA).Should().Be("earlier export");
            File.Exists(pieceB).Should().BeFalse("nothing is written once a piece would be replaced");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Separated_MeshFormat_DeclinedReplace_LeavesEveryFileAlone()
    {
        var (mesh, record) = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var pieceB = Path.Combine(dir, "bolus_B.stl");
            File.WriteAllText(pieceB, "earlier export");

            IReadOnlyList<string>? asked = null;
            var export = new ExportMesh(_engine).Execute(mesh, record, path, overwrite: true,
                confirmReplace: paths => { asked = paths; return false; });

            export.IsFailure.Should().BeTrue();
            export.Error.Should().Be(MeshErrors.ExportCancelled);
            asked.Should().Equal(pieceB);
            File.ReadAllText(pieceB).Should().Be("earlier export");
            File.Exists(Path.Combine(dir, "bolus_A.stl")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Separated_MeshFormat_ConfirmedReplace_WritesEveryPiece()
    {
        var (mesh, record) = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var pieceA = Path.Combine(dir, "bolus_A.stl");
            File.WriteAllText(pieceA, "earlier export");

            var export = new ExportMesh(_engine).Execute(mesh, record, path, overwrite: true,
                confirmReplace: _ => true);

            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");
            File.ReadAllText(pieceA).Should().NotBe("earlier export");
            File.Exists(Path.Combine(dir, "bolus_B.stl")).Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Combined_MeshFormat_WritesSingleFile()
    {
        var (mesh, record) = PartedMould(PartingResultMode.Combined);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var export = new ExportMesh(_engine).Execute(mesh, record, path, overwrite: true);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            File.Exists(path).Should().BeTrue();
            File.Exists(Path.Combine(dir, "bolus_A.stl")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Separated_3mf_WritesSingleProjectFile()
    {
        var (mesh, record) = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.3mf");
            var export = new ExportMesh(_engine).Execute(mesh, record, path, overwrite: true);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            File.Exists(path).Should().BeTrue("3MF is a single project file even when separated");
            File.Exists(Path.Combine(dir, "bolus_A.3mf")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }
}
