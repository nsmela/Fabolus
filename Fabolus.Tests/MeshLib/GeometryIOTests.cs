using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.IO;
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
}
