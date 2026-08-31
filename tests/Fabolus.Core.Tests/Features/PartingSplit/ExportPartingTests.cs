using System.IO;
using System.Numerics;
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
    /// returning the single parted mesh.</summary>
    private IMesh PartedMould(PartingResultMode mode)
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphere.IsSuccess.Should().BeTrue();

        var workspace = Workspace.CreateEmpty();
        workspace = workspace.AddMesh(sphere.Value).Value;
        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;

        var mould = new GenerateMould(_engine).Execute(workspace, bodyId,
            new ConvexMouldDefinition(OffsetXY: 3.0, OffsetBottom: 3.0, OffsetTop: 3.0) { TargetMeshId = bodyId });
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");
        workspace = mould.Value;

        var applied = new SplitMouldFeature(_engine).ExecuteCut(
            workspace, bodyId,
            PartingLineParameters.Default with { Source = PartingLineSource.Silhouette },
            PartingMeshParameters.Default, mode);
        applied.IsSuccess.Should().BeTrue(applied.IsFailure ? applied.Error.Description : "");

        var pieceMeta = applied.Value.MeshMetadataList.Single(m => m.Id != bodyId);
        return applied.Value.GetMesh(pieceMeta.Id).Value;
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
        var mesh = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var export = new ExportMesh(_engine).Execute(mesh, path, overwrite: true);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            File.Exists(Path.Combine(dir, "bolus_A.stl")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "bolus_B.stl")).Should().BeTrue();
            File.Exists(path).Should().BeFalse("a separated export writes per-half files, not the base name");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Combined_MeshFormat_WritesSingleFile()
    {
        var mesh = PartedMould(PartingResultMode.Combined);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.stl");
            var export = new ExportMesh(_engine).Execute(mesh, path, overwrite: true);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            File.Exists(path).Should().BeTrue();
            File.Exists(Path.Combine(dir, "bolus_A.stl")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Separated_3mf_WritesSingleProjectFile()
    {
        var mesh = PartedMould(PartingResultMode.Separated);
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "bolus.3mf");
            var export = new ExportMesh(_engine).Execute(mesh, path, overwrite: true);
            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");

            File.Exists(path).Should().BeTrue("3MF is a single project file even when separated");
            File.Exists(Path.Combine(dir, "bolus_A.3mf")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }
}
