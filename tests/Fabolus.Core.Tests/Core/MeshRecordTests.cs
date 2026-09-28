using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using FluentAssertions;
using System;
using Xunit;

namespace Fabolus.Tests.Core;

public class MeshRecordTests
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

    private static MeshRecord Empty() => new() { Id = Guid.NewGuid(), Name = "Test" };

    [Fact]
    public void ForImport_NamesTheEntryAndGivesItAnIdentity()
    {
        var record = MeshRecord.ForImport("eye_bolus");

        record.Name.Should().Be("eye_bolus");
        record.CreatedBy.Should().Be("Import");
        record.Id.Should().NotBeEmpty();
        record.Commands.Should().BeEmpty();
        record.BaseMesh.Should().BeNull();
    }

    [Fact]
    public void ForImport_GivesEachEntryItsOwnIdentity()
    {
        // Two components separated out of one file are two entries, not one.
        MeshRecord.ForImport("bolus").Id.Should().NotBe(MeshRecord.ForImport("bolus").Id);
    }

    [Fact]
    public void WithCommand_ReturnsANewRecordAndLeavesTheOriginalAlone()
    {
        var original = Empty();

        var updated = original.WithCommand(new FakeCommandA());

        updated.Should().NotBeSameAs(original);
        original.Commands.Should().BeEmpty();
        updated.Commands.Should().ContainSingle().Which.Should().BeOfType<FakeCommandA>();
    }

    [Fact]
    public void WithCommand_KeepsTheIdentityItWasGiven()
    {
        // The whole point of the record: nothing an operation records can change who the entry is.
        var original = Empty();

        var updated = original.WithCommand(new FakeCommandA()).WithCommand(new FakeDownstreamCommand());

        updated.Id.Should().Be(original.Id);
        updated.Name.Should().Be(original.Name);
    }

    [Fact]
    public void WithCommand_ReplacesExistingOfSameTypeAndMovesToEnd()
    {
        // Mirrors the "rotate, smooth, rotate again" scenario: re-recording a command of a
        // type that already exists should replace it (not stack it) and move it to the end
        // of the order, matching the "one net value per feature" overwrite behaviour.
        var record = Empty()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeCommandB())
            .WithCommand(new FakeCommandA());

        record.Commands.Should().HaveCount(2);
        record.Commands[0].Should().BeOfType<FakeCommandB>();
        record.Commands[1].Should().BeOfType<FakeCommandA>();
    }

    [Fact]
    public void WithoutCommand_RemovesCommandOfGivenType()
    {
        var record = Empty()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeCommandB())
            .WithoutCommand<FakeCommandA>();

        record.Commands.Should().ContainSingle().Which.Should().BeOfType<FakeCommandB>();
    }

    [Fact]
    public void WithCommand_ClearsExistingHigherPriorityCommand()
    {
        var record = Empty()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeDownstreamCommand())
            .WithCommand(new FakeCommandB());

        // Recording FakeCommandB (priority 10) invalidates the downstream (priority 20)
        // command that depended on the geometry it just changed.
        record.Commands.Should().HaveCount(2);
        record.Commands.Should().Contain(c => c is FakeCommandA);
        record.Commands.Should().Contain(c => c is FakeCommandB);
        record.Commands.Should().NotContain(c => c is FakeDownstreamCommand);
    }

    [Fact]
    public void WithCommand_DoesNotClearSamePriorityCommands()
    {
        var record = Empty()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeCommandB());

        // Both priority 10 - siblings, neither invalidates the other.
        record.Commands.Should().HaveCount(2);
    }

    [Fact]
    public void WithoutCommand_CascadesToHigherPriorityCommands()
    {
        var record = Empty()
            .WithCommand(new FakeCommandA())
            .WithCommand(new FakeDownstreamCommand())
            .WithoutCommand<FakeCommandA>();

        record.Commands.Should().BeEmpty();
    }

    [Fact]
    public void WithoutCommand_NoOpWhenTypeNotPresent()
    {
        var record = Empty()
            .WithCommand(new FakeCommandB())
            .WithCommand(new FakeDownstreamCommand());

        var updated = record.WithoutCommand<FakeCommandA>();

        updated.Should().BeSameAs(record);
        updated.Commands.Should().HaveCount(2);
    }

    [Fact]
    public void Command_FindsTheRecordedCommandOrNothing()
    {
        var record = Empty().WithCommand(new FakeCommandA());

        record.Command<FakeCommandA>().Should().NotBeNull();
        record.Command<FakeCommandB>().Should().BeNull();
    }
}
