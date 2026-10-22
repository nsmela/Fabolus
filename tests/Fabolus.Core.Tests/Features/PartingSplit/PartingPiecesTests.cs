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
/// Specks a parting cut chips off are not pieces of the mould (issue #88). Counted as pieces, they
/// let a cut that left the mould whole pass for a split, and a separated export wrote each one to a
/// file of its own: users splitting flat_vulva got nine STLs.
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
            mould.Value, bodyId,
            PartingLineParameters.Default with
            {
                Source = PartingLineSource.ExtrusionBorder,
                PullDirection = Vector3.UnitY,
                NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
            },
            PartingMeshParameters.Default with
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
            },
            PartingResultMode.Separated);
        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");

        var piece = cut.Value.Records.Single(r => r.Id != bodyId);
        var components = _engine.Evaluators.SeparateComponents(cut.Value.GetMesh(piece.Id).Value).Value;

        components.Should().HaveCount(2);
        components.Select(PartingPieces.Volume).Min().Should().BeGreaterThan(
            components.Sum(PartingPieces.Volume) * 0.3, "both are real halves of the mould");
    }
}
