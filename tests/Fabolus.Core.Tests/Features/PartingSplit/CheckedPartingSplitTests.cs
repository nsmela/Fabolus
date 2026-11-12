using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The checked split on the saved moulds, through the calls the app makes, under the recipe the view
/// fixes for new work.
///
/// <para>
/// One body for each thing the check can say: a split that passes clean, one returned with a warning,
/// and two refused for different reasons. The refusals are bodies that 1.1.0 returned a split for, so
/// each also runs the same recipe unchecked and expects what 1.1.0 gave - a project saved then carries
/// no request to be checked, and has to reopen as it was.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class CheckedPartingSplitTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;
    private readonly PartingMeshFeature _sut;

    public CheckedPartingSplitTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
    }

    private static PartingLineParameters Line => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    /// <summary>The view's recipe, with the check as asked.</summary>
    private static PartingMeshParameters Recipe(PartingMeshSweep sweep, PartingSplitVerification verification) =>
        PartingMeshParameters.Default with
        {
            AxisSource = PartingMeshAxisSource.PartingLine,
            Sweep = sweep,
            SplitMethod = PartingSplitMethod.SeveredComponents,
            Thickening = PartingMeshThickening.Extrude,
            Depth = PartingMeshParameters.DefaultDepth,
            FlangeMaxSlopeDeg = 80.0,
            NormalFollowMm = 100.0,
            NormalSmoothingPasses = 5,
            Verification = verification,
            Axis = Vector3.UnitY,
        };

    private MouldMesh Mould(string file) =>
        SavedMoulds.Mould(SavedMoulds.Open(_engine, _fixture.GetAssetPath("3mf/" + file)).Value).Value;

    [Fact]
    public void Standard_OnTheLoft_IsReturnedWithNothingToWarnOf()
    {
        var cut = _sut.CutMouldReporting(
            Mould("test bolus standard.3mf"), Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.Checked));

        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");
        cut.Value.Report.WasChecked.Should().BeTrue();
        cut.Value.Report.Warnings.Should().BeEmpty();
        _engine.Evaluators.SeparateComponents(cut.Value.Mesh).Value.Should().HaveCount(2);
    }

    /// <summary>
    /// Chin's line doubles back at one corner seen along its axis, and the loft folds there: 7 mm² of
    /// mating face turned over. The halves still slide apart - the fold is too small to hook - so the
    /// split is returned, and says what it found.
    /// </summary>
    [Fact]
    public void Chin_OnTheLoft_IsReturned_AndSaysItsFlangeDoublesBack()
    {
        var cut = _sut.CutMouldReporting(
            Mould("chin.3mf"), Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.Checked));

        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");
        cut.Value.Report.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be("Split.FlangeDoublesBack");
        cut.Value.Report.Describe().Should().NotBeEmpty("the view shows this text beside the halves");
    }

    /// <summary>
    /// The split this work started from. The loft folds through the body on larynx_large and severs
    /// nothing, and 1.1.0 returned it as a success: one piece holding 99.6% of the mould and a crumb
    /// thick enough not to count as a speck.
    /// </summary>
    [Fact]
    public void LarynxLarge_OnTheLoft_IsRefusedWhenChecked_AndReturnedAsItWasWhenNot()
    {
        var mould = Mould("larynx_large.3mf");

        var @checked = _sut.CutMouldReporting(mould, Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.Checked));
        @checked.IsFailure.Should().BeTrue("a cutter that folds through itself thousands of times cuts nothing that can be trusted");
        @checked.Error.Code.Should().Be("Split.CutterCrossesItself");

        var asSaved = _sut.CutMouldReporting(mould, Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.None));
        asSaved.IsSuccess.Should().BeTrue("a recipe that never asked to be checked replays as it was committed");
        asSaved.Value.Report.Should().Be(PartingSplitReport.Unchecked);

        double whole = PartingPieces.Volume(mould.Mesh);
        var shares = _engine.Evaluators.SeparateComponents(asSaved.Value.Mesh).Value
            .Select(piece => PartingPieces.Volume(piece) / whole)
            .OrderByDescending(share => share)
            .ToList();
        shares[0].Should().BeGreaterThan(0.99, "which is the wrong answer the check exists to stop, kept only for old projects");
    }

    /// <summary>
    /// Two points of the loft's inner edge come out of the body on larynx_small. The cut still severs,
    /// so this is a split that was returned and is now refused; the same recipe unchecked returns it.
    /// </summary>
    [Fact]
    public void LarynxSmall_OnTheLoft_IsRefusedForItsSeal_AndReturnedAsItWasWhenNot()
    {
        var mould = Mould("larynx_small.3mf");

        var @checked = _sut.CutMouldReporting(mould, Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.Checked));
        @checked.IsFailure.Should().BeTrue();
        @checked.Error.Code.Should().Be("Split.SealBreached");

        var asSaved = _sut.CutMouldReporting(mould, Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.None));
        asSaved.IsSuccess.Should().BeTrue(asSaved.IsFailure ? asSaved.Error.Description : "");
    }

    /// <summary>
    /// Next and Apply have to hand back the same halves. They did not: Next took the biggest component
    /// as one half and everything else as the other, and Apply cleared specks and counted pieces. Both
    /// go through the checked split now, so what is reviewed is what is committed.
    /// </summary>
    [Fact]
    public void ThePreviewAndTheCommit_HandBackTheSameHalves()
    {
        var mould = Mould("scalp.3mf");
        var recipe = Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.Checked);

        // As the view builds it, stage by stage.
        var body = _sut.GetBodyMesh(mould).Value;
        var line = _sut.GeneratePartingLineFromBody(body, Line).Value;
        var resolved = PartingMeshFeature.ResolveAxis(line, recipe).Value;
        var contour = _sut.GenerateOuterContour(mould, resolved).Value;
        var flange = _sut.GenerateFlangeSurface(line, contour, resolved, body).Value;
        var previewed = _sut.SplitVerified(mould, flange, body, line, resolved);
        previewed.IsSuccess.Should().BeTrue(previewed.IsFailure ? previewed.Error.Description : "");

        // As Apply and a replay build it, from the recipe alone.
        var committed = _sut.CutMouldReporting(mould, Line, recipe);
        committed.IsSuccess.Should().BeTrue(committed.IsFailure ? committed.Error.Description : "");
        var replayed = _sut.SplitMould(mould, Line, recipe);
        replayed.IsSuccess.Should().BeTrue(replayed.IsFailure ? replayed.Error.Description : "");

        var pieces = _engine.Evaluators.SeparateComponents(committed.Value.Mesh).Value
            .OrderByDescending(PartingPieces.Volume).ToList();
        pieces.Should().HaveCount(2, "scalp's first cut leaves four specks, and the commit clears them");

        PartingPieces.Volume(previewed.Value.Positive).Should().BeApproximately(PartingPieces.Volume(pieces[0]), 1e-6);
        PartingPieces.Volume(previewed.Value.Negative).Should().BeApproximately(PartingPieces.Volume(pieces[1]), 1e-6);
        PartingPieces.Volume(replayed.Value.Positive).Should().BeApproximately(PartingPieces.Volume(pieces[0]), 1e-6);
        PartingPieces.Volume(replayed.Value.Negative).Should().BeApproximately(PartingPieces.Volume(pieces[1]), 1e-6);
    }

    /// <summary>
    /// The check travels with the recipe by name, and a recipe written before it existed comes back
    /// unchecked - which is the whole of why an older project still opens.
    /// </summary>
    [Fact]
    public void TheCheck_IsStoredByName_AndARecipeWithoutIt_IsUnchecked()
    {
        var command = new CutCommand(
            Line, Recipe(PartingMeshSweep.MouldLoft, PartingSplitVerification.Checked), PartingResultMode.Separated);

        string json = MeshCommandSerializer.Serialize([command]);
        json.Should().Contain("\"Verification\":\"Checked\"");

        var restored = MeshCommandSerializer.Deserialize(json).Value.OfType<CutCommand>().Single();
        restored.MeshParameters.Verification.Should().Be(PartingSplitVerification.Checked);

        var older = MeshCommandSerializer.Deserialize(json.Replace("\"Verification\":\"Checked\",", "")).Value
            .OfType<CutCommand>().Single();
        older.MeshParameters.Verification.Should().Be(PartingSplitVerification.None);
    }
}
