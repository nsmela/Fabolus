using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fabolus.Tests.Core;

public class MeshMetadataTests
{
    private sealed record FakeCommandA : IMeshCommand {
        public int Priority => CommandPriority.Transform;
        public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) => Result<IMesh>.Success(mesh);
    }

    private sealed record FakeCommandB : IMeshCommand {
        public int Priority => CommandPriority.Transform;
        public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) => Result<IMesh>.Success(mesh);
    }

    // Priority 20 (like Mould), representing something downstream of the priority-10 fakes.
    private sealed record FakeDownstreamCommand : IMeshCommand {
        public int Priority => CommandPriority.Mould;
        public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) => Result<IMesh>.Success(mesh);
    }

    [Fact]
    public void WithProperty_SetsPropertyAndReturnsNewInstance()
    {
        var original = new MeshMetadata();
        var key = new MetadataKey<string>("TestKey");

        var updated = original.WithProperty(key, "TestValue");

        updated.Should().NotBeSameAs(original);
        updated.GetProperty(key).Value.Should().Be("TestValue");
        original.GetProperty(key).HasNoValue.Should().BeTrue();
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

    [Fact]
    public void WithCommand_AppendsNewCommandType()
    {
        var metadata = new MeshMetadata().WithCommand(new FakeCommandA());

        metadata.Commands.Should().ContainSingle().Which.Should().BeOfType<FakeCommandA>();
    }

    [Fact]
    public void WithCommand_ReplacesExistingOfSameTypeAndMovesToEnd()
    {
        // Mirrors the "rotate, smooth, rotate again" scenario: re-recording a command of a
        // type that already exists should replace it (not stack it) and move it to the end
        // of the order, matching today's "one net value per feature" overwrite behavior.
        var metadata = new MeshMetadata()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeCommandB())
            .WithCommand(new FakeCommandA());

        metadata.Commands.Should().HaveCount(2);
        metadata.Commands[0].Should().BeOfType<FakeCommandB>();
        metadata.Commands[1].Should().BeOfType<FakeCommandA>();
    }

    [Fact]
    public void WithoutCommand_RemovesCommandOfGivenType()
    {
        var metadata = new MeshMetadata()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeCommandB())
            .WithoutCommand<FakeCommandA>();

        metadata.Commands.Should().ContainSingle().Which.Should().BeOfType<FakeCommandB>();
    }

    [Fact]
    public void WithCommand_ClearsExistingHigherPriorityCommand()
    {
        var metadata = new MeshMetadata()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeDownstreamCommand())
            .WithCommand(new FakeCommandB());

        // Recording FakeCommandB (priority 10) invalidates the downstream (priority 20)
        // command that depended on the geometry it just changed.
        metadata.Commands.Should().HaveCount(2);
        metadata.Commands.Should().Contain(c => c is FakeCommandA);
        metadata.Commands.Should().Contain(c => c is FakeCommandB);
        metadata.Commands.Should().NotContain(c => c is FakeDownstreamCommand);
    }

    [Fact]
    public void WithCommand_DoesNotClearSamePriorityCommands()
    {
        var metadata = new MeshMetadata()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeCommandB());

        // Both priority 10 - siblings, neither invalidates the other.
        metadata.Commands.Should().HaveCount(2);
    }

    [Fact]
    public void WithoutCommand_CascadesToHigherPriorityCommands()
    {
        var metadata = new MeshMetadata()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeDownstreamCommand())
            .WithoutCommand<FakeCommandA>();

        metadata.Commands.Should().BeEmpty();
    }

    [Fact]
    public void WithoutCommand_NoOpWhenTypeNotPresent()
    {
        var metadata = new MeshMetadata()
            .WithCommand(new FakeCommandB())
            .WithCommand(new FakeDownstreamCommand());

        var updated = metadata.WithoutCommand<FakeCommandA>();

        updated.Should().BeSameAs(metadata);
        updated.Commands.Should().HaveCount(2);
    }
}
