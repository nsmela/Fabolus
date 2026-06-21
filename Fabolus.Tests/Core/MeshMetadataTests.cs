using Fabolus.Core.Geometry.Metadata;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fabolus.Tests.Core;

public class MeshMetadataTests
{
    [Fact]
    public void WithProperty_SetsPropertyAndReturnsNewInstance()
    {
        var original = new MeshMetadata();
        var key = new MetadataKey<string>("TestKey");

        var updated = original.WithProperty(key, "TestValue");

        updated.Should().NotBeSameAs(original);
        updated.GetProperty(key).Value.Should().Be("TestValue");
        original.GetProperty(key).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void WithProperties_AppliesMultipleProperties()
    {
        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "TestName")
             .Set(CoreKeys.CreatedBy, "User"));

        metadata.Name.Should().Be("TestName");
        metadata.CreatedBy.Value.Should().Be("User");
    }

    [Fact]
    public void FromFileName_SetsCorrectProperties()
    {
        var filePath = "C:/x/eye_bolus.stl";
        var metadata = MeshMetadata.FromFileName(filePath);

        metadata.Name.Should().Be("eye_bolus");
        metadata.CreatedBy.Value.Should().Be("Import");
        metadata.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void GetRequired_ThrowsIfMissing()
    {
        var metadata = new MeshMetadata();
        var key = new MetadataKey<string>("NonExistent");

        Action act = () => metadata.GetRequired(key);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Helpers_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var derivedFrom = Guid.NewGuid();
        
        var metadata = new MeshMetadata()
            .WithId(id)
            .WithName("MyMesh")
            .WithDerivedFrom(derivedFrom)
            .WithCreatedBy("Generator");

        metadata.Id.Should().Be(id);
        metadata.Name.Should().Be("MyMesh");
        metadata.DerivedFrom.Value.Should().Be(derivedFrom);
        metadata.CreatedBy.Value.Should().Be("Generator");
    }
}
