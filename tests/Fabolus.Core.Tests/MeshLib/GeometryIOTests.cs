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
}
