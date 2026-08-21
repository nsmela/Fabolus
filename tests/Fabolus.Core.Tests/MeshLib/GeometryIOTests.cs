using Fabolus.Core.Geometry;
using Fabolus.Core.Features.Transforms;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.IO;
using System.Numerics;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class GeometryIOTests
{
    private readonly GeometryEngineFixture _fixture;

    public GeometryIOTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("sphere.stl")]
    [InlineData("eye_bolus.stl")]
    [InlineData("small test.stl")]
    public void Import_SupportedFixture_ReturnsSuccessAndPopulatedMesh(string filename)
    {
        var path = _fixture.GetAssetPath(filename);
        
        var result = _fixture.Engine.IO.Import(path);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThan(0);
        mesh.Metadata.Name.Should().Be(Path.GetFileNameWithoutExtension(filename));
        mesh.Metadata.CreatedBy.Value.Should().Be("Import");
    }

    [Fact]
    public void Import_UnsupportedExtension_ReturnsUnsupportedFormat()
    {
        var result = _fixture.Engine.IO.Import("invalid_file.xyz");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("MRIO.FileNotFound");
    }

    [Fact]
    public void Export_AndReImport_RoundTripsSuccessfully()
    {
        var original = _fixture.LoadStl("sphere.stl");
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var exportPath = Path.Combine(tempDir, "export_test.stl");
            
            // Export
            var exportResult = _fixture.Engine.IO.Export(original, exportPath);
            exportResult.IsSuccess.Should().BeTrue();

            // Re-import
            var importResult = _fixture.Engine.IO.Import(exportPath);
            importResult.IsSuccess.Should().BeTrue();
            
            var imported = importResult.Value;
            imported.VertexCount.Should().Be(original.VertexCount);
            imported.TriangleCount.Should().Be(original.TriangleCount);
            
            // Export existing with overwrite: false should fail
            var failExport = _fixture.Engine.IO.Export(original, exportPath, overwrite: false);
            failExport.IsFailure.Should().BeTrue();
            failExport.Error.Code.Should().Be("IO.FileExists");

            // Export existing with overwrite: true should succeed
            var successExport = _fixture.Engine.IO.Export(original, exportPath, overwrite: true);
            successExport.IsSuccess.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Export_3MF_PreservesMetadata_RoundTripsSuccessfully()
    {
        var original = _fixture.LoadStl("sphere.stl");
        
        // Add metadata
        var command = new Fabolus.Core.Features.Smoothing.SmoothSettings(5, 3.5f, 0.2f, 1.5f, 0.5f);
        
        // Create a distinctly different base mesh so we can detect if they get swapped
        var baseMesh = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 50, 32).Value;
        
        var metadata = original.Metadata
            .WithCommand(command)
            .WithBaseMesh(baseMesh);
        var meshWithMetadata = original.WithMetadata(metadata);

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var exportPath = Path.Combine(tempDir, "export_test.3mf");
            
            // Export
            var exportResult = _fixture.Engine.IO.Export(meshWithMetadata, exportPath);
            exportResult.IsSuccess.Should().BeTrue();

            // Re-import
            var importResult = _fixture.Engine.IO.Import(exportPath);
            if (!importResult.IsSuccess)
            {
                throw new System.Exception($"Import failed: {importResult.Error?.Description}");
            }
            
            var imported = importResult.Value;
            imported.VertexCount.Should().Be(original.VertexCount);
            imported.TriangleCount.Should().Be(original.TriangleCount);
            
            // Verify Metadata
            imported.Metadata.Commands.Should().HaveCount(1);
            var importedCommand = imported.Metadata.Commands[0] as Fabolus.Core.Features.Smoothing.SmoothSettings;
            importedCommand.Should().NotBeNull();
            importedCommand.Iterations.Should().Be(5);
            importedCommand.Intensity.Should().Be(3.5f);
            importedCommand.Inflation.Should().Be(0.2f);
            importedCommand.RemeshRatio.Should().Be(1.5f);
            importedCommand.Resolution.Should().Be(0.5f);

            imported.Metadata.HasBaseMesh.Should().BeTrue();
            var importedBaseMesh = imported.Metadata.GetBaseMesh().Value;
            importedBaseMesh.VertexCount.Should().Be(baseMesh.VertexCount);
            importedBaseMesh.TriangleCount.Should().Be(baseMesh.TriangleCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// chin_legacy_smooth.3mf was saved when the smoothing command was named SmoothCommand.
    /// The name lookup used to miss and drop the command, leaving a mesh whose baked geometry
    /// was smoothed but whose history said otherwise - so replaying to the Transform stage
    /// (what the smoothing view renders) produced an unsmoothed model.
    /// </summary>
    [Fact]
    public void Import_3MF_WithLegacyCommandName_PreservesSmoothingInHistory()
    {
        var path = _fixture.GetAssetPath("chin_legacy_smooth.3mf");

        var result = _fixture.Engine.IO.Import(path);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;

        mesh.Metadata.Commands.Should().HaveCount(3);
        mesh.Metadata.Commands.Should().ContainSingle(c => c is RotateCommand);
        mesh.Metadata.Commands.Should().ContainSingle(c => c is Fabolus.Core.Features.Moulds.ConcaveMouldDefinition);

        var smoothing = mesh.Metadata.Commands.OfType<Fabolus.Core.Features.Smoothing.SmoothSettings>().Single();
        smoothing.Iterations.Should().Be(1);
        smoothing.Inflation.Should().Be(0.1f);
        smoothing.RemeshRatio.Should().Be(2.0f);
    }

    /// <summary>
    /// The import-time centring offset is persisted so later features can reference how far a
    /// mesh sits from its authored position - it has to survive the 3mf round trip intact.
    /// </summary>
    [Fact]
    public void Export_3MF_RoundTripsTranslateCommandValues()
    {
        var original = _fixture.LoadStl("sphere.stl");
        var command = new TranslateCommand(new Vector3(12.5f, -3.25f, 0.75f));
        var baseMesh = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 50, 32).Value;

        var meshWithMetadata = original.WithMetadata(original.Metadata
            .WithCommand(command)
            .WithBaseMesh(baseMesh));

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var exportPath = Path.Combine(tempDir, "translate_round_trip.3mf");
            _fixture.Engine.IO.Export(meshWithMetadata, exportPath).IsSuccess.Should().BeTrue();

            var importResult = _fixture.Engine.IO.Import(exportPath);
            importResult.IsSuccess.Should().BeTrue();

            var imported = importResult.Value.Metadata.Commands.OfType<TranslateCommand>().Single();
            imported.Translation.X.Should().Be(12.5f);
            imported.Translation.Y.Should().Be(-3.25f);
            imported.Translation.Z.Should().Be(0.75f);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Export_3MF_RoundTripsTextEmbossCommandValues()
    {
        var original = _fixture.LoadStl("sphere.stl");
        var decal = new Fabolus.Core.Features.Emboss.TextDecal
        {
            Text = "FAB3MF",
            CapHeight = 5.0f,
            Depth = 0.8f,
            Operation = Fabolus.Core.Features.Emboss.EmbossOperation.Emboss,
            RotationDeg = 45f,
            Anchor = new Vector3(1, 2, 3),
            AnchorNormal = Vector3.UnitZ
        };
        var command = new Fabolus.Core.Features.Emboss.TextEmbossCommand(decal);
        var baseMesh = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 50, 32).Value;

        var meshWithMetadata = original.WithMetadata(original.Metadata
            .WithCommand(command)
            .WithBaseMesh(baseMesh));

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var exportPath = Path.Combine(tempDir, "text_emboss_round_trip.3mf");
            _fixture.Engine.IO.Export(meshWithMetadata, exportPath).IsSuccess.Should().BeTrue();

            var importResult = _fixture.Engine.IO.Import(exportPath);
            importResult.IsSuccess.Should().BeTrue();

            var imported = importResult.Value.Metadata.Commands.OfType<Fabolus.Core.Features.Emboss.TextEmbossCommand>().Single();
            imported.Decal.Text.Should().Be("FAB3MF");
            imported.Decal.CapHeight.Should().Be(5.0f);
            imported.Decal.Depth.Should().Be(0.8f);
            imported.Decal.RotationDeg.Should().Be(45f);
            imported.Decal.Anchor.X.Should().Be(1f);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Import_3MF_WithUnresolvableCommandName_FailsInsteadOfDroppingIt()
    {
        var result = Fabolus.Core.Geometry.Metadata.MeshCommandRegistry.ResolveType("NotARealCommand");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Metadata.UnknownCommand");
    }
}
