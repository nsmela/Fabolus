using System.IO;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A parting line settled by hand, carried in the recipe: that the recipe rebuilds exactly that line,
/// that a split made from it is cut along it, that it survives a save file, and that it is refused on a
/// body it no longer lies on.
/// </summary>
[Collection("GeometryEngine collection")]
public class EditedPartingLineTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public EditedPartingLineTests(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    private static PartingLineParameters Traced => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    private static PartingMeshParameters Recipe => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.MouldLoft,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        Depth = PartingMeshParameters.DefaultDepth,
        FlangeMaxSlopeDeg = 80.0,
        NormalFollowMm = 100.0,
        NormalSmoothingPasses = 5,
        Verification = PartingSplitVerification.Checked,
        Axis = Vector3.UnitY,
    };

    private MouldMesh Mould(string file) =>
        SavedMoulds.Mould(SavedMoulds.Open(_engine, _fixture.GetAssetPath("3mf/" + file)).Value).Value;

    // ---------------------------------------------------------------- the line

    [Fact]
    public void ARecipeCarryingAnEditedLine_RebuildsExactlyThatLine()
    {
        var body = _sut.GetBodyMesh(Mould("test bolus standard.3mf")).Value;
        var edited = Smoothed(body);

        var rebuilt = _sut.GeneratePartingLineFromBody(body, Traced with { Edited = EditedPartingLine.From(edited) });

        rebuilt.IsSuccess.Should().BeTrue(rebuilt.IsFailure ? rebuilt.Error.Description : "");
        rebuilt.Value.Loops.Should().HaveCount(edited.Loops.Count);
        for (int l = 0; l < edited.Loops.Count; l++)
            rebuilt.Value.Loops[l].Should().Equal(edited.Loops[l], "the approved line is rebuilt point for point, not approximately");
    }

    /// <summary>
    /// The tolerance a stored line is held to assumes an edited line sits on its body to begin with.
    /// This is that assumption, measured on every edit the view offers.
    /// </summary>
    [Theory]
    [InlineData("chin.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("nose.3mf")]
    [InlineData("larynx_large.3mf")]
    public void EveryEditTheViewOffers_LeavesTheLineOnTheBody(string file)
    {
        var body = _sut.GetBodyMesh(Mould(file)).Value;
        var traced = _sut.GeneratePartingLineFromBody(body, Traced).Value;
        var seeded = _sut.BeginPartingLineEdit(body, traced).Value;
        using var geodesic = new PartingTools(_engine).CreateSurfaceGeodesic(body.Mesh).Value;

        var edits = new Dictionary<string, PartingLineEdit>
        {
            ["seeded"] = seeded,
            ["smoothed"] = Each(seeded, rim => PartingLineEditor.Smooth(rim.Line, rim.Graph)),
            ["handle moved"] = Each(seeded, rim =>
                PartingLineEditor.Move(rim.Line, 0, rim.Line.Spans[0].Points[rim.Line.Spans[0].Points.Count / 3], rim.Graph)),
            ["handle added"] = Each(seeded, rim =>
                PartingLineEditor.Insert(rim.Line, rim.Line.Spans[0].Points[rim.Line.Spans[0].Points.Count / 2], rim.Graph)),
            ["handle removed"] = Each(seeded, rim => PartingLineEditor.Remove(rim.Line, 0, rim.Graph)),
            ["free geodesic"] = Each(seeded, rim => PartingLineEditor.Retrace(rim.Line, rim.Graph, geodesic)),
        };

        var index = _engine.Spatial.IndexFor(body.Mesh).Value;
        foreach (var (name, edit) in edits)
        {
            double furthest = edit.ToPartingLine().Loops.SelectMany(l => l)
                .Max(p => index.ClosestPoint(p).Value.Distance);
            _out.WriteLine($"{file} {name}: furthest off the body {furthest:E2} mm");

            furthest.Should().BeLessThan(PartingMeshFeature.EditedLineToleranceMm / 10.0,
                $"'{name}' has to leave the line on the body with room to spare, or a line edited and replayed on the same body would be refused");
        }
    }

    [Fact]
    public void ALineStoredOnABodyThatHasSinceMoved_IsRefused()
    {
        var body = _sut.GetBodyMesh(Mould("test bolus standard.3mf")).Value;
        var stored = Traced with { Edited = EditedPartingLine.From(Smoothed(body)) };

        // The same body, a millimetre from where the line was approved - what a re-smoothed or
        // re-positioned bolus looks like to a line that cannot follow it.
        var moved = BodyMesh.Create(_engine.Transforms.Translate(body.Mesh, new Vector3(0.0, 1.0, 0.0)).Value).Value;

        var rebuilt = _sut.GeneratePartingLineFromBody(moved, stored);

        rebuilt.IsFailure.Should().BeTrue();
        rebuilt.Error.Code.Should().Be("PartingLine.EditedLineOffBody");
        rebuilt.Error.Description.Should().Contain("set the line again");
    }

    [Fact]
    public void SomethingStoredThatIsNotALine_IsRefused()
    {
        var body = _sut.GetBodyMesh(Mould("test bolus standard.3mf")).Value;

        var twoPoints = new EditedPartingLine([[0, 0, 0, 1, 0, 0]]);
        _sut.GeneratePartingLineFromBody(body, Traced with { Edited = twoPoints })
            .Error.Should().Be(MeshErrors.InvalidPartingLine);

        var notANumber = new EditedPartingLine([[0, 0, 0, 1, 0, 0, double.NaN, 1, 0]]);
        _sut.GeneratePartingLineFromBody(body, Traced with { Edited = notANumber })
            .Error.Should().Be(MeshErrors.InvalidPartingLine);
    }

    // ---------------------------------------------------------------- the split

    /// <summary>
    /// The defect itself, at the level it did its harm. Apply and every later replay cut from the
    /// recipe alone; with the edit in the recipe they cut the halves the preview showed, and without
    /// it - which is all a recipe could hold before - they cut different ones.
    /// </summary>
    [Fact]
    public void ASplitFromTheRecipe_IsCutAlongTheEditedLine_AsThePreviewWas()
    {
        var mould = Mould("test bolus standard.3mf");
        var body = _sut.GetBodyMesh(mould).Value;
        var edited = Smoothed(body);

        // As the view previews it: the edited line, stage by stage.
        var resolved = PartingMeshFeature.ResolveAxis(edited, Recipe).Value;
        var contour = _sut.GenerateOuterContour(mould, resolved).Value;
        var flange = _sut.GenerateFlangeSurface(edited, contour, resolved, body).Value;
        var previewed = _sut.SplitVerified(mould, flange, body, edited, resolved);
        previewed.IsSuccess.Should().BeTrue(previewed.IsFailure ? previewed.Error.Description : "");

        // As Apply commits it: the recipe and nothing else.
        var committed = _sut.SplitMould(mould, Traced with { Edited = EditedPartingLine.From(edited) }, Recipe);
        committed.IsSuccess.Should().BeTrue(committed.IsFailure ? committed.Error.Description : "");

        PartingPieces.Volume(committed.Value.Positive).Should().BeApproximately(PartingPieces.Volume(previewed.Value.Positive), 1e-6);
        PartingPieces.Volume(committed.Value.Negative).Should().BeApproximately(PartingPieces.Volume(previewed.Value.Negative), 1e-6);

        // And what the recipe committed while it could not hold the edit.
        var withoutTheEdit = _sut.SplitMould(mould, Traced, Recipe);
        withoutTheEdit.IsSuccess.Should().BeTrue(withoutTheEdit.IsFailure ? withoutTheEdit.Error.Description : "");
        Math.Abs(PartingPieces.Volume(withoutTheEdit.Value.Positive) - PartingPieces.Volume(previewed.Value.Positive))
            .Should().BeGreaterThan(1.0, "the automatic line cuts different halves, which is what the user used to be handed");
    }

    // ---------------------------------------------------------------- the save file

    [Fact]
    public void TheEditedLine_SurvivesASaveFile_AndARecipeWithoutOneHasNone()
    {
        var body = _sut.GetBodyMesh(Mould("test bolus standard.3mf")).Value;
        var line = Traced with { Edited = EditedPartingLine.From(Smoothed(body)) };
        var command = new CutCommand(line, Recipe, PartingResultMode.Separated);

        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32).Value;
        var record = MeshRecord.ForImport("sphere").WithBaseMesh(sphere) with { Commands = [command] };

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        try
        {
            string path = Path.Combine(directory.FullName, "edited.3mf");
            new ExportMesh(_engine).Execute(sphere, record, path).IsSuccess.Should().BeTrue();

            var reopened = new ImportMesh(_engine).Execute(Workspace.CreateEmpty(), path);
            reopened.IsSuccess.Should().BeTrue(reopened.IsFailure ? reopened.Error.Description : "");

            var restored = reopened.Value.GetActiveRecord().Value.Command<CutCommand>()!;
            restored.LineParameters.Edited.Should().Be(line.Edited, "every coordinate comes back the double it was written as");
            restored.LineParameters.Should().Be(line, "and the recipe is a record: holding the same line, it is the same recipe");
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        string older = MeshCommandSerializer.Serialize([new CutCommand(Traced, Recipe, PartingResultMode.Separated)]);
        older.Should().Contain("\"Edited\":null");
        MeshCommandSerializer.Deserialize(older.Replace("\"Edited\":null,", "")).Value
            .OfType<CutCommand>().Single().LineParameters.Edited.Should().BeNull("a recipe written before the field existed traces its line");
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>The body's own line with Smooth pressed once, as the view would hold it.</summary>
    private PartingLine Smoothed(BodyMesh body)
    {
        var traced = _sut.GeneratePartingLineFromBody(body, Traced).Value;
        var seeded = _sut.BeginPartingLineEdit(body, traced).Value;
        return Each(seeded, rim => PartingLineEditor.Smooth(rim.Line, rim.Graph)).ToPartingLine();
    }

    private static PartingLineEdit Each(PartingLineEdit edit, Func<PartingRimEdit, SectionedPartingLine> change)
    {
        for (int rim = 0; rim < edit.Rims.Count; rim++)
            edit = edit.With(rim, change(edit.Rims[rim]));
        return edit;
    }
}
