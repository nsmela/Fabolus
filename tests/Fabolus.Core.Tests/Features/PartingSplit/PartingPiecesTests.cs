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
/// Specks a parting cut leaves are not pieces of the mould (issue #88). Counted as pieces, they let a
/// cut that left the mould whole pass for a split, and a separated export wrote each one to a file of
/// its own: users splitting flat_vulva got nine STLs. The cut now moves the flange off them, and
/// anything still left is told from a piece by its thickness.
/// </summary>
[Collection("GeometryEngine collection")]
public class PartingPiecesTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly IGeometryEngine _engine;

    public PartingPiecesTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    private IMesh Box(Vector3 min, Vector3 max) => _engine.Generators.GenerateBox(min, max).Value;

    // 1000 mm3, 800 mm3 and a thousandth of one.
    private IMesh Big => Box(new Vector3(0, 0, 0), new Vector3(10, 10, 10));
    private IMesh Half => Box(new Vector3(20, 0, 0), new Vector3(28, 10, 10));
    private IMesh Speck => Box(new Vector3(0, 0, 20), new Vector3(0.1, 0.1, 20.1));

    /// <summary>The recipe PartingSplitViewModel fixes for new work.</summary>
    private static PartingLineParameters ViewLine => PartingLineParameters.Default with
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
        NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
    };

    private static PartingMeshParameters ViewMesh => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.MouldLoft,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        FlangeMaxSlopeDeg = 80.0,
        NormalFollowMm = 100.0,
        RawFlange = false,
        NormalSmoothingPasses = 5,
        Axis = Vector3.UnitY,
    };

    /// <summary>The meshes laid end to end in one, as a cut leaves its pieces.</summary>
    private IMesh Joined(params IMesh[] pieces)
    {
        var joined = pieces[0];
        foreach (var piece in pieces.Skip(1))
        {
            joined = _engine.CreateMesh(
                joined.Vertices.AddRange(piece.Vertices),
                joined.Triangles.AddRange(piece.Triangles.Select(i => i + joined.Vertices.Length)),
                joined.Metadata).Value;
        }

        return joined;
    }

    [Fact]
    public void Substantial_DropsSpecks_AndKeepsTheHalvesBiggestFirst()
    {
        var pieces = PartingPieces.Substantial([Speck, Half, Big]);

        pieces.Select(PartingPieces.Volume).Should().Equal([1000.0, 800.0],
            (actual, expected) => Math.Abs(actual - expected) < 1e-6);
    }

    [Fact]
    public void Substantial_OneBodyAndSpecks_IsOnePiece()
    {
        PartingPieces.Substantial([Big, Speck, Speck]).Should().HaveCount(1);
    }

    /// <summary>
    /// A piece is told from a speck by thickness, not by its share: a small chunk of wall is still a
    /// piece of the mould. A 3 mm cube beside a 100 mL slab is under a thousandth of the whole.
    /// </summary>
    [Fact]
    public void Substantial_KeepsASmallSolidPiece()
    {
        var slab = Box(new Vector3(0, 0, 0), new Vector3(100, 100, 10));
        var chunk = Box(new Vector3(0, 0, 20), new Vector3(3, 3, 23));

        PartingPieces.Substantial([slab, chunk, Speck]).Should().HaveCount(2);
    }

    /// <summary>
    /// A flange lying 0.05 mm over a box's top face, the way one lies along an extruded shell's top
    /// beside the parting line, with a speck under it: the flange points round the speck go into
    /// the box past the cutter's depth, and the rest stay where they were.
    /// </summary>
    [Fact]
    public void SinkFlangeAround_MovesOnlyTheSkimmingFlangeNearASpeck()
    {
        var body = BodyMesh.Create(Box(new Vector3(0, 0, 0), new Vector3(20, 20, 10))).Value;

        var points = new List<Vector3>();
        var corners = new List<int>();
        const int nx = 26, ny = 11;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                points.Add(new Vector3(5 + i, 5 + j, 10.05));
        for (int j = 0; j + 1 < ny; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = j * nx + i;
                corners.AddRange([a, a + 1, a + nx + 1, a, a + nx + 1, a + nx]);
            }
        var flange = _engine.CreateMesh([.. points], [.. corners], Big.Metadata).Value;
        var speck = Box(new Vector3(12, 10, 10.0), new Vector3(12.5, 10.5, 10.04));

        var sunk = new PartingMeshFeature(_engine).SinkFlangeAround(flange, body, [speck], depth: 0.1);

        sunk.IsSuccess.Should().BeTrue(sunk.IsFailure ? sunk.Error.Description : "");
        sunk.Value.Moved.Should().BeGreaterThan(0);
        for (int k = 0; k < points.Count; k++)
        {
            var before = points[k];
            var after = sunk.Value.Surface.Vertices[k];
            bool nearSpeck = speck.Vertices.Any(p => p.DistanceTo(before) <= 2.0);
            if (nearSpeck && before.X < 20)
                after.Z.Should().BeApproximately(10.0 - 0.2, 1e-6, $"({before.X}, {before.Y}) skims the top face beside the speck");
            else
                after.Should().Be(before, $"({before.X}, {before.Y}) is not beside a speck over the face");
        }
    }

    [Fact]
    public void SeparatedExport_LeavesSpecksOut()
    {
        var record = MeshRecord.ForImport("mould").WithCommand(new CutCommand(
            PartingLineParameters.Default, PartingMeshParameters.Default, PartingResultMode.Separated));

        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "mould.stl");
            var export = new ExportMesh(_engine).Execute(Joined(Speck, Big, Speck, Half), record, path, overwrite: true);

            export.IsSuccess.Should().BeTrue(export.IsFailure ? export.Error.Description : "");
            export.Value.Should().Equal(Path.Combine(dir, "mould_A.stl"), Path.Combine(dir, "mould_B.stl"));
            Directory.GetFiles(dir).Should().HaveCount(2);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// scalp_bolus with the view's recipe parts 57/43 and leaves seven specks along the rim; a
    /// separated export of it used to be nine files.
    /// </summary>
    [Fact]
    public void ViewRecipeCut_OnScalp_IsTwoPiecesWithNoSpecks()
    {
        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), _fixture.LoadStl("scalp_bolus.stl"));
        var mould = new GenerateMould(_engine).Execute(workspace, bodyId,
            MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0) with { TargetMeshId = bodyId });
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        var cut = new SplitMouldFeature(_engine).ExecuteCut(
            mould.Value, bodyId, ViewLine, ViewMesh, PartingResultMode.Separated);
        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");

        var piece = cut.Value.Records.Single(r => r.Id != bodyId);
        var components = _engine.Evaluators.SeparateComponents(cut.Value.GetMesh(piece.Id).Value).Value;

        components.Should().HaveCount(2);
        components.Select(PartingPieces.Volume).Min().Should().BeGreaterThan(
            components.Sum(PartingPieces.Volume) * 0.3, "both are real halves of the mould");
    }

    /// <summary>
    /// The specks are cleared at their source, by moving the flange off them, not only dropped
    /// afterwards: the cut itself comes back in two pieces and nothing else.
    /// </summary>
    [Fact]
    public void CutClear_OnScalp_LeavesNoSpecksToDrop()
    {
        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), _fixture.LoadStl("scalp_bolus.stl"));
        var built = new GenerateMould(_engine).Execute(workspace, bodyId,
            MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0) with { TargetMeshId = bodyId });
        built.IsSuccess.Should().BeTrue(built.IsFailure ? built.Error.Description : "");

        var mould = MouldMesh.Create(built.Value.GetMesh(bodyId).Value, built.Value.GetRecord(bodyId).Value).Value;
        var feature = new PartingMeshFeature(_engine);
        var line = feature.GeneratePartingLine(mould, ViewLine).Value;
        var settings = PartingMeshFeature.ResolveAxis(line, ViewMesh).Value;
        var body = feature.GetBodyMesh(mould).Value;
        var surface = feature.GenerateFlangeSurface(
            line, feature.GenerateOuterContour(mould, settings).Value, settings, body).Value;

        // As built, the flange's cutter leaves specks - this is what the correction is for.
        var asBuilt = feature.CutMouldWith(mould, feature.ExtrudeFlange(surface, settings).Value).Value;
        _engine.Evaluators.SeparateComponents(asBuilt).Value.Count(PartingPieces.IsSpeck).Should().BeGreaterThan(0);

        var clear = feature.CutClear(mould, surface, body, settings);

        clear.IsSuccess.Should().BeTrue(clear.IsFailure ? clear.Error.Description : "");
        clear.Value.Components.Should().HaveCount(2);
        clear.Value.Components.Should().NotContain(c => PartingPieces.IsSpeck(c));
    }
}
