using BasicResults;
using Fabolus.Core.Geometry;
using System.Text.Json.Serialization;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>Whether a parting split is checked before it is handed back.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingSplitVerification
{
    /// <summary>
    /// Return whatever the cut produced, as every split did before the check existed.
    ///
    /// <para>
    /// The zero value and the record default, and it has to be. A saved project replays its split from
    /// the recipe, and a recipe written before this option arrives without it; were the check the
    /// default, a project that opened yesterday could refuse to open today over a split its owner
    /// committed and may already have printed. New work is checked because the view asks for it.
    /// </para>
    /// </summary>
    None,

    /// <summary>
    /// Check the parting mesh before it cuts and the pieces after, and refuse a split that is wrong
    /// rather than return it - see <see cref="PartingSplitVerifier"/>.
    /// </summary>
    Checked,
}

/// <summary>Something a checked split is handed back in spite of, for the user to weigh.</summary>
/// <param name="Code">Stable, for anything that has to tell one warning from another.</param>
/// <param name="Description">What is wrong and what it means for the mould, in the user's terms.</param>
public sealed record PartingSplitWarning(string Code, string Description);

/// <summary>What the check found on a split it let through.</summary>
/// <param name="WasChecked">False for a recipe that did not ask, where an empty list means nothing.</param>
public sealed record PartingSplitReport(bool WasChecked, IReadOnlyList<PartingSplitWarning> Warnings)
{
    /// <summary>The report of a split nobody checked.</summary>
    public static PartingSplitReport Unchecked { get; } = new(false, []);

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>The warnings as one passage, for a place that shows text.</summary>
    public string Describe() => string.Join(" ", Warnings.Select(w => w.Description));
}

/// <summary>A parting split that passed its check, with what the check noticed.</summary>
public sealed record VerifiedParting(IMesh Positive, IMesh Negative, PartingSplitReport Report);

/// <summary>A mould cut in place - both halves in one mesh - with what the check noticed.</summary>
public sealed record PartingCut(IMesh Mesh, PartingSplitReport Report);

/// <summary>The reasons a checked split is refused. Each says what to do about it.</summary>
public static class PartingSplitErrors
{
    public static Error SealBreached(int points, double worstMm) => new("Split.SealBreached",
        $"The parting mesh's inner edge comes out of the body at {points} point(s), by up to {worstMm:F1} mm, " +
        "so the cut would leave a bridge of mould across the parting. Move the parting line there, or choose " +
        "a different parting mesh.");

    public static Error CutterCrossesItself(int crossings) => new("Split.CutterCrossesItself",
        $"The parting mesh folds through itself in {crossings} place(s), so the cut it would make cannot be " +
        "trusted. Smooth or move the parting line where it turns sharply, or choose a different parting mesh.");

    /// <summary>
    /// Says which way the cutter falls short, because the two are different faults. An open edge is a
    /// hole in the solid. An edge shared by more than two faces is where two parts of the surface
    /// touch along a line - closed, but with no single inside either side of it - which is what the
    /// skin bisector leaves on most bodies: measured, 2 to 38 such edges and never an open one.
    /// </summary>
    public static Error CutterNotSolid(int openEdges, int sharedEdges) => new("Split.CutterNotSolid",
        $"The parting mesh is not a clean closed solid: it has {openEdges} open edge(s) and {sharedEdges} " +
        "edge(s) where more than two faces meet, so the cut it would make cannot be trusted. Choose a " +
        "different parting mesh.");

    public static Error LeftSpecks(int specks) => new("Split.Specks",
        $"The cut left {specks} thin sliver(s) of mould between the halves that could not be cleared. Move the " +
        "parting line off the face it runs along, or choose a different parting mesh.");

    public static Error Uneven(double largerPct, double smallerPct) => new("Split.Uneven",
        $"The cut left the mould almost whole: one piece holds {largerPct:F0}% of it and the other " +
        $"{smallerPct:F1}%. The parting mesh does not reach across the mould. Choose a different parting mesh, " +
        "or move the parting line.");

    public static Error ExtraPieces(int pieces) => new("Split.ExtraPieces",
        $"The cut divided the mould into {pieces} pieces rather than two. Choose a different parting mesh, or " +
        "move the parting line.");

    public static readonly Error HalfNotSolid = new("Split.HalfNotSolid",
        "A half of the mould came back with an open or non-manifold surface, so it is not a printable solid. " +
        "Choose a different parting mesh, or move the parting line.");

    public static Error VolumeNotConserved(double missingMm3) => new("Split.VolumeNotConserved",
        $"The halves and the cut between them do not add up to the mould: {Math.Abs(missingMm3):F1} mm³ is " +
        "unaccounted for, so the cut cannot be trusted. Choose a different parting mesh.");

    public static Error MouldChanged(int points, double worstMm) => new("Split.MouldChanged",
        $"The cut changed the mould away from the parting, at {points} point(s) and by up to {worstMm:F2} mm. " +
        "The cavity the bolus is cast in may be among them, so the split is not safe to use. Choose a " +
        "different parting mesh.");

    public static PartingSplitWarning FlangeDoublesBack(double areaMm2) => new("Split.FlangeDoublesBack",
        $"About {areaMm2:F0} mm² of the surface the halves meet on turns back over itself along the parting " +
        "axis, so the halves may catch there when they are pulled apart.");

    public static PartingSplitWarning HalvesFoul(double overlapMm3, double atMm) => new("Split.HalvesFoul",
        $"The halves overlap by about {overlapMm3:F0} mm³ when slid {atMm:0.#} mm apart along the parting axis, " +
        "so they lock together and will not pull straight apart. The mould may have to be worked or cut off " +
        "the cast.");
}

/// <summary>
/// Checks a parting split: the parting mesh before it cuts, and the pieces after.
///
/// <para>
/// A boolean always returns something. A cutter that does not reach across the mould subtracts to a
/// mould with a slot in it; one that folds through itself subtracts to whatever the fold happened to
/// enclose; and both come back as valid meshes with nothing to say they are not two halves. Counting
/// pieces caught the first of those and nothing else - measured on larynx_large, the default sweep
/// returned a "split" of one piece holding 99.6% of the mould and a crumb, because the crumb was
/// thick enough not to be a speck. So the result is measured against what a split has to be, and
/// refused by name when it is not.
/// </para>
///
/// <para>
/// Two things are warnings rather than refusals: a flange that doubles back, and halves that foul
/// when pulled. They mean the mould locks. That is a real defect, but on a body like larynx_large no
/// sweep avoids it, and a mould that has to be worked off the cast is still more use than no mould;
/// so the split is returned and the report says so, where the user cannot miss it.
/// </para>
/// </summary>
internal sealed class PartingSplitVerifier
{
    /// <summary>
    /// The smallest share of the mould a piece may hold and still be a half. The most lopsided good
    /// split measured is 63/37; the crumb this exists to catch was 0.1%.
    /// </summary>
    public const double LeastHalfShare = 0.05;

    /// <summary>
    /// How far the pieces and the kerf may fall short of the mould, as a share of it. Every split
    /// measured that was otherwise sound came within a hundredth of a cubic millimetre; this is ten
    /// parts per million, a few cubic millimetres on a head-sized mould, which is the rounding of
    /// three booleans and not a missing sliver.
    /// </summary>
    public const double VolumeTolerance = 1e-5;

    /// <summary>The least the volume allowance is ever taken to be, in mm³, for a very small mould.</summary>
    public const double LeastVolumeToleranceMm3 = 1.0;

    /// <summary>
    /// What "away from the cut" means, measured from the cutter. The cutter is a tenth of a millimetre
    /// thick and the speck correction moves the flange by little more, so a millimetre clears both.
    /// </summary>
    public const double AwayFromCutMm = 1.0;

    /// <summary>
    /// How far a point may sit from the surface it should still be on. Well above the boolean's own
    /// rounding, well below anything that would show on a cast.
    /// </summary>
    public const double UnchangedMm = 0.01;

    private readonly IGeometryEngine _engine;
    private readonly PartingTools _parting;

    public PartingSplitVerifier(IGeometryEngine engine, PartingTools parting)
    {
        _engine = engine;
        _parting = parting;
    }

    /// <summary>
    /// Whether the flange's inner edge is inside the body all the way round. A point outside is a
    /// bridge of mould the cut leaves across the parting.
    /// </summary>
    /// <remarks>
    /// Not asked of the skin bisector. The inspection finds the inner edge by the line's footprint
    /// along the axis, and the bisector has no edge of that kind - it runs on inside the body - so it
    /// reports breaches on a surface that has none.
    /// </remarks>
    public Result CheckSeal(IMesh surface, BodyMesh body, PartingLine line, PartingMeshParameters parameters)
    {
        if (parameters.Sweep == PartingMeshSweep.SkinBisector) return Result.Success();

        var seal = _parting.InspectFlangeSeal(surface, body.Mesh, parameters.Axis, line.Loops[0]);
        if (seal.IsFailure) return seal.Error;

        int breached = seal.Value.Count(point => !point.IsSealed);
        return breached == 0
            ? Result.Success()
            : PartingSplitErrors.SealBreached(breached, seal.Value.Max(point => point.SignedDistance));
    }

    /// <summary>
    /// Whether the cutter is a clean closed solid - no open edge, no edge more than two faces share -
    /// that does not pass through itself.
    /// </summary>
    public Result CheckCutter(IMesh cutter)
    {
        var topology = _engine.Evaluators.ValidateTopology(cutter);
        if (topology.IsFailure) return topology.Error;
        if (!topology.Value.IsWatertight)
            return PartingSplitErrors.CutterNotSolid(topology.Value.BoundaryEdgeCount, topology.Value.NonManifoldEdgeCount);

        var crossings = _engine.Evaluators.CountSelfIntersections(cutter);
        if (crossings.IsFailure) return crossings.Error;

        return crossings.Value == 0 ? Result.Success() : PartingSplitErrors.CutterCrossesItself(crossings.Value);
    }

    /// <summary>
    /// Whether what the cut left is two halves of the mould and nothing else, and what stands in the
    /// way of pulling them apart.
    /// </summary>
    /// <param name="surface">The flange the cutter was thickened from, for the fold measurement.</param>
    /// <param name="components">Everything the subtraction left, specks included.</param>
    public Result<PartingSplitReport> CheckPieces(
        MouldMesh mould, BodyMesh body, IMesh surface, IMesh cutter,
        IReadOnlyList<IMesh> components, Vector3 axis)
    {
        int specks = components.Count(PartingPieces.IsSpeck);
        var pieces = PartingPieces.Substantial(components);

        // Fewer than two first: specks beside one piece are a cut that did not sever, and that is the
        // thing to say about it.
        if (pieces.Count < 2) return MeshErrors.SplitProducedSinglePiece;
        if (specks > 0) return PartingSplitErrors.LeftSpecks(specks);
        if (pieces.Count > 2) return PartingSplitErrors.ExtraPieces(pieces.Count);

        double mouldVolume = PartingPieces.Volume(mould.Mesh);
        double larger = PartingPieces.Volume(pieces[0]);
        double smaller = PartingPieces.Volume(pieces[1]);
        if (mouldVolume <= 0.0) return MeshErrors.InvalidMesh;
        if (smaller < LeastHalfShare * mouldVolume)
            return PartingSplitErrors.Uneven(100.0 * larger / mouldVolume, 100.0 * smaller / mouldVolume);

        foreach (var piece in pieces)
        {
            var topology = _engine.Evaluators.ValidateTopology(piece);
            if (topology.IsFailure) return topology.Error;
            if (!topology.Value.IsWatertight || !topology.Value.IsManifold) return PartingSplitErrors.HalfNotSolid;
        }

        // The mould is the pieces and what the cutter took out of it, and nothing else.
        var kerf = _engine.Booleans.Intersect(mould.Mesh, cutter);
        if (kerf.IsFailure) return kerf.Error;

        double missing = mouldVolume - PartingPieces.Volume(kerf.Value) - larger - smaller;
        if (Math.Abs(missing) > Math.Max(LeastVolumeToleranceMm3, VolumeTolerance * mouldVolume))
            return PartingSplitErrors.VolumeNotConserved(missing);

        var unchanged = CheckUnchanged(mould.Mesh, cutter, pieces);
        if (unchanged.IsFailure) return unchanged.Error;

        // From here on the split is returned. What is left is whether it comes apart.
        var warnings = new List<PartingSplitWarning>(2);

        var flange = PartingSeparability.OfFlange(_engine, surface, body.Mesh, mould.Mesh, axis);
        if (flange.IsFailure) return flange.Error;
        if (!flange.Value.IsSingleValued)
            warnings.Add(PartingSplitErrors.FlangeDoublesBack(flange.Value.TurnedOverMm2 + flange.Value.DoubledOverMm2));

        var pulled = PartingSeparability.OfHalves(_engine, pieces, axis);
        if (pulled.IsFailure) return pulled.Error;
        if (!pulled.Value.Parts)
            warnings.Add(PartingSplitErrors.HalvesFoul(pulled.Value.WorstOverlapMm3, pulled.Value.AtMm));

        return new PartingSplitReport(true, warnings);
    }

    /// <summary>
    /// Whether the mould's own surface is where it was, away from the cut. Asked both ways: every
    /// mould vertex should still lie on a piece, and every piece vertex on the mould. The first catches
    /// surface the cut took away, the second surface it added.
    /// </summary>
    private Result CheckUnchanged(IMesh mould, IMesh cutter, IReadOnlyList<IMesh> pieces)
    {
        var cutterIndex = _engine.Spatial.BuildIndex(cutter);
        if (cutterIndex.IsFailure) return cutterIndex.Error;
        using var nearCut = cutterIndex.Value;

        var mouldIndex = _engine.Spatial.IndexFor(mould);
        if (mouldIndex.IsFailure) return mouldIndex.Error;

        int moved = 0;
        double worst = 0.0;

        var mouldVertices = mould.Vertices;
        var mouldToCut = nearCut.SignedDistances(mouldVertices);
        var mouldToPieces = new double[mouldVertices.Length];
        Array.Fill(mouldToPieces, double.MaxValue);

        foreach (var piece in pieces)
        {
            var pieceIndex = _engine.Spatial.BuildIndex(piece);
            if (pieceIndex.IsFailure) return pieceIndex.Error;
            using var onPiece = pieceIndex.Value;

            var distances = onPiece.SignedDistances(mouldVertices);
            for (int i = 0; i < distances.Length; i++)
                mouldToPieces[i] = Math.Min(mouldToPieces[i], Math.Abs(distances[i]));

            var pieceVertices = piece.Vertices;
            var pieceToCut = nearCut.SignedDistances(pieceVertices);
            var pieceToMould = mouldIndex.Value.SignedDistances(pieceVertices);
            for (int i = 0; i < pieceVertices.Length; i++)
            {
                if (Math.Abs(pieceToCut[i]) < AwayFromCutMm) continue;

                double off = Math.Abs(pieceToMould[i]);
                if (off <= UnchangedMm) continue;
                moved++;
                worst = Math.Max(worst, off);
            }
        }

        for (int i = 0; i < mouldVertices.Length; i++)
        {
            if (Math.Abs(mouldToCut[i]) < AwayFromCutMm || mouldToPieces[i] <= UnchangedMm) continue;
            moved++;
            worst = Math.Max(worst, mouldToPieces[i]);
        }

        return moved == 0 ? Result.Success() : PartingSplitErrors.MouldChanged(moved, worst);
    }
}
