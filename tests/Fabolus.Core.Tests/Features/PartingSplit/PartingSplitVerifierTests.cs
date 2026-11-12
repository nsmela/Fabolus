using System.Collections.Immutable;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Pins what <see cref="PartingSplitVerifier"/> refuses, one refusal to a test, on a shape simple
/// enough that the right answer is not in doubt: a mould round a sphere, cut with flat slabs.
///
/// <para>
/// Each case is a way a boolean hands back valid meshes that are not two halves of the mould. Before
/// the check, every one of them was returned as a split.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class PartingSplitVerifierTests
{
    private readonly IGeometryEngine _engine;
    private readonly PartingSplitVerifier _sut;

    public PartingSplitVerifierTests(GeometryEngineFixture fixture)
    {
        _engine = fixture.Engine;
        _sut = new PartingSplitVerifier(_engine, new PartingTools(_engine));
    }

    // ---------------------------------------------------------------- the cut that is right

    [Fact]
    public void ASlabThroughTheMiddle_IsAccepted_WithNothingToWarnOf()
    {
        var (mould, body) = SphereMould();
        var cutter = Slab(0.0);

        var report = _sut.CheckPieces(mould, body, Sheet(0.0), cutter, CutWith(mould, cutter), Vector3.UnitY);

        report.IsSuccess.Should().BeTrue(report.IsFailure ? report.Error.Description : "");
        report.Value.WasChecked.Should().BeTrue();
        report.Value.Warnings.Should().BeEmpty("a flat cut across a convex mould parts along its own normal");
    }

    // ---------------------------------------------------------------- pieces

    [Fact]
    public void AMouldTheCutDidNotSever_IsRefused()
    {
        var (mould, body) = SphereMould();

        var report = _sut.CheckPieces(mould, body, Sheet(0.0), Slab(0.0), [mould.Mesh], Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Should().Be(MeshErrors.SplitProducedSinglePiece);
    }

    /// <summary>
    /// The case that was passing for a split: the mould all but whole, and beside it a second piece
    /// thick enough not to be a speck and far too small to be a half. Measured on larynx_large with the
    /// loft, 99.6% and 0.1%. Handed over directly rather than cut, because a cut that produces it needs
    /// a cutter folded through a body, and the crumb is the whole of what the check has to see.
    /// </summary>
    [Fact]
    public void AMouldLeftWholeBesideACrumb_IsRefusedAsUneven()
    {
        var (mould, body) = SphereMould();
        var crumb = _engine.Generators.GenerateBox(new Vector3(20, 20, 20), new Vector3(22, 22, 22)).Value;
        PartingPieces.IsSpeck(crumb).Should().BeFalse("a crumb thicker than a speck is exactly what used to count as a half");

        var report = _sut.CheckPieces(mould, body, Sheet(0.0), Slab(0.0), [mould.Mesh, crumb], Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Split.Uneven");
    }

    [Fact]
    public void ThreePieces_AreRefused()
    {
        var (mould, body) = SphereMould();
        var both = _engine.Booleans.Union([Slab(-4.0), Slab(4.0)]).Value;

        var report = _sut.CheckPieces(mould, body, Sheet(4.0), both, CutWith(mould, both), Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Split.ExtraPieces");
    }

    [Fact]
    public void ASliverLeftBetweenTheHalves_IsRefused()
    {
        var (mould, body) = SphereMould();
        var cutter = Slab(0.0);
        var sliver = _engine.Generators.GenerateBox(new Vector3(-2, 20, -2), new Vector3(2, 20.05, 2)).Value;

        var report = _sut.CheckPieces(mould, body, Sheet(0.0), cutter, [.. CutWith(mould, cutter), sliver], Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Split.Specks");
    }

    [Fact]
    public void AHalfWithAHoleInItsSurface_IsRefused()
    {
        var (mould, body) = SphereMould();
        var cutter = Slab(0.0);
        var components = CutWith(mould, cutter).ToList();
        components[0] = WithoutFirstFace(components[0]);

        var report = _sut.CheckPieces(mould, body, Sheet(0.0), cutter, components, Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Should().Be(PartingSplitErrors.HalfNotSolid);
    }

    // ---------------------------------------------------------------- the mould itself

    /// <summary>
    /// Halves from a cut ten times thicker than the cutter the check is told about: a millimetre of
    /// mould has gone that the cutter does not account for.
    /// </summary>
    [Fact]
    public void HalvesThatDoNotAddUpToTheMould_AreRefused()
    {
        var (mould, body) = SphereMould();

        var report = _sut.CheckPieces(
            mould, body, Sheet(0.0), Slab(0.0, thickness: 0.1), CutWith(mould, Slab(0.0, thickness: 1.0)), Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Split.VolumeNotConserved");
    }

    /// <summary>
    /// Volume says nothing about where the material is. A half moved a third of a millimetre sideways
    /// encloses exactly what it did, and its cavity is no longer the cavity the bolus was designed in.
    /// </summary>
    [Fact]
    public void AHalfThatIsNoLongerWhereTheMouldWas_IsRefused()
    {
        var (mould, body) = SphereMould();
        var cutter = Slab(0.0);
        var components = CutWith(mould, cutter).ToList();
        components[0] = _engine.Transforms.Translate(components[0], new Vector3(0.3, 0.0, 0.0)).Value;

        var report = _sut.CheckPieces(mould, body, Sheet(0.0), cutter, components, Vector3.UnitY);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Split.MouldChanged");
    }

    // ---------------------------------------------------------------- the cutter

    [Fact]
    public void ACleanClosedCutter_IsAdmitted() =>
        _sut.CheckCutter(Slab(0.0)).IsSuccess.Should().BeTrue();

    [Fact]
    public void ACutterWithAFaceMissing_IsRefused()
    {
        var check = _sut.CheckCutter(WithoutFirstFace(Slab(0.0)));

        check.IsFailure.Should().BeTrue();
        check.Error.Code.Should().Be("Split.CutterNotSolid");
        check.Error.Description.Should().Contain("3 open edge(s)", "a missing triangle leaves its three edges open");
    }

    /// <summary>
    /// Closed, and still not a solid: two boxes sharing one edge, so four faces meet along it. This is
    /// the fault the skin bisector's cutter has on most bodies, and the message has to say it is not a
    /// hole.
    /// </summary>
    [Fact]
    public void ACutterWhoseSurfaceTouchesItselfAlongAnEdge_IsRefused()
    {
        var one = _engine.Generators.GenerateBox(new Vector3(0, 0, 0), new Vector3(10, 1, 10)).Value;
        var other = _engine.Generators.GenerateBox(new Vector3(10, 1, 0), new Vector3(20, 2, 10)).Value;
        var welded = WeldedAt(Joined(one, other));

        var topology = _engine.Evaluators.ValidateTopology(welded).Value;
        topology.BoundaryEdgeCount.Should().Be(0);
        topology.NonManifoldEdgeCount.Should().BeGreaterThan(0);

        var check = _sut.CheckCutter(welded);

        check.IsFailure.Should().BeTrue();
        check.Error.Code.Should().Be("Split.CutterNotSolid");
        check.Error.Description.Should().Contain("0 open edge(s)");
    }

    [Fact]
    public void ACutterThatPassesThroughItself_IsRefused()
    {
        // Two slabs crossing at right angles, laid in one mesh without being united: closed, and
        // through itself along the whole line they share.
        var upright = _engine.Generators.GenerateBox(new Vector3(-0.05, -30, -30), new Vector3(0.05, 30, 30)).Value;
        var check = _sut.CheckCutter(Joined(Slab(0.0), upright));

        check.IsFailure.Should().BeTrue();
        check.Error.Code.Should().Be("Split.CutterCrossesItself");
    }

    // ---------------------------------------------------------------- fixtures

    private (MouldMesh Mould, BodyMesh Body) SphereMould()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32).Value;
        var (workspace, id) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), sphere);

        var moulded = new GenerateMould(_engine).Execute(workspace, id,
            new ConvexMouldDefinition(OffsetXY: 3.0, OffsetBottom: 3.0, OffsetTop: 3.0) { TargetMeshId = id });
        moulded.IsSuccess.Should().BeTrue(moulded.IsFailure ? moulded.Error.Description : "");

        var mould = MouldMesh.Create(moulded.Value.GetMesh(id).Value, moulded.Value.GetRecord(id).Value).Value;
        return (mould, BodyMesh.Create(_engine, mould).Value);
    }

    /// <summary>A flat cutter across everything, square to Y.</summary>
    private IMesh Slab(double y, double thickness = 0.1) =>
        _engine.Generators.GenerateBox(
            new Vector3(-30, y - (thickness * 0.5), -30), new Vector3(30, y + (thickness * 0.5), 30)).Value;

    /// <summary>The flange a slab at <paramref name="y"/> would have been thickened from.</summary>
    private IMesh Sheet(double y)
    {
        ImmutableArray<Vector3> vertices =
            [new(-30, y, -30), new(30, y, -30), new(30, y, 30), new(-30, y, 30)];
        return _engine.CreateMesh(vertices, [0, 2, 1, 0, 3, 2], MeshMetadata.Named("sheet")).Value;
    }

    private IReadOnlyList<IMesh> CutWith(MouldMesh mould, IMesh cutter) =>
        _engine.Evaluators.SeparateComponents(_engine.Booleans.Subtract(mould.Mesh, cutter).Value).Value;

    private IMesh WithoutFirstFace(IMesh mesh) =>
        _engine.CreateMesh(mesh.Vertices, mesh.Triangles.RemoveRange(0, 3), mesh.Metadata).Value;

    /// <summary>The mesh with vertices at the same place made one vertex, so touching parts share edges.</summary>
    private IMesh WeldedAt(IMesh mesh)
    {
        var index = new Dictionary<Vector3, int>();
        var vertices = ImmutableArray.CreateBuilder<Vector3>();
        var map = new int[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (!index.TryGetValue(mesh.Vertices[i], out int at))
            {
                at = index[mesh.Vertices[i]] = vertices.Count;
                vertices.Add(mesh.Vertices[i]);
            }
            map[i] = at;
        }

        return _engine.CreateMesh(
            vertices.ToImmutable(), [.. mesh.Triangles.Select(corner => map[corner])], mesh.Metadata).Value;
    }

    private IMesh Joined(IMesh a, IMesh b) =>
        _engine.CreateMesh(
            a.Vertices.AddRange(b.Vertices),
            a.Triangles.AddRange(b.Triangles.Select(corner => corner + a.VertexCount)),
            a.Metadata).Value;
}
