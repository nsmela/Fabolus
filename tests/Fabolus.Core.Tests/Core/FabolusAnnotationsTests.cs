using Fabolus.Core.Geometry.Metadata;
using FluentAssertions;
using GeometryEngine.Core.Geometry;
using Xunit;

namespace Fabolus.Tests.Core;

/// <summary>
/// The carry rules are the contract the engine relies on: it asks what survives an operation and
/// trusts the answer, so these pin down what "survives" means for each kind of operation.
/// </summary>
public class FabolusAnnotationsTests
{
    private static readonly MeshStatistics Stats =
        new(Volume: 1, SurfaceArea: 6, BoundsMin: new(0, 0, 0), BoundsMax: new(1, 1, 1), VertexCount: 8, TriangleCount: 12);

    private static readonly TopologyValidation Topology =
        new(BoundaryEdgeCount: 0, NonManifoldEdgeCount: 0, DegenerateTriangleCount: 0,
            DuplicateVertexCount: 0, InconsistentWindingEdgeCount: 0, DuplicateFaceCount: 0, ShellCount: 1);

    private static FabolusAnnotations Both() => new(Stats, Topology);

    [Fact]
    public void Transform_KeepsTopologyBecauseConnectivityIsUntouched()
    {
        var carried = Both().Carry(MeshOperation.Transform);

        carried.Should().BeOfType<FabolusAnnotations>()
            .Which.Topology.Should().Be(Topology);
    }

    [Fact]
    public void Transform_DropsStatsBecauseTheBoundsMoved()
    {
        // Translation moves the bounds; a scale also changes the volume and surface area. Keeping
        // the old figures would be worse than having none - a stale number reads as a fresh one.
        var carried = Both().Carry(MeshOperation.Transform);

        carried.Should().BeOfType<FabolusAnnotations>()
            .Which.Stats.Should().BeNull();
    }

    [Fact]
    public void Transform_CarriesNothingWhenThereWasNoTopologyToKeep()
    {
        var carried = new FabolusAnnotations(Stats).Carry(MeshOperation.Transform);

        carried.Should().BeNull();
    }

    [Fact]
    public void Rebuild_DropsEverything()
    {
        // Offset, decimate, smooth and repair all replace the surface: triangle counts, volume
        // and the topology audit can all differ afterwards.
        Both().Carry(MeshOperation.Rebuild).Should().BeNull();
    }

    [Fact]
    public void Combine_DropsEverything()
    {
        // This is the case the whole refactor exists for: a boolean's result is neither operand,
        // so nothing either of them measured describes it.
        Both().Carry(MeshOperation.Combine).Should().BeNull();
    }

    [Fact]
    public void AnnotationsRideOnTheMeshAndReadBackTyped()
    {
        var mesh = ImmutableMesh.Empty.WithAnnotations(Both());

        mesh.Stats().Should().Be(Stats);
        mesh.Topology().Should().Be(Topology);
    }

    [Fact]
    public void AMeshWithNoAnnotationsReadsAsAbsentRatherThanThrowing()
    {
        // A mesh straight out of a boolean or an import carries nothing yet. That is a normal
        // state - the old code minted an empty property bag here and then threw on a missing Id.
        ImmutableMesh.Empty.Stats().Should().BeNull();
        ImmutableMesh.Empty.Topology().Should().BeNull();
        ImmutableMesh.Empty.Annotations().Should().NotBeNull();
    }
}
